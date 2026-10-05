using System.Collections.Concurrent;
using System.Diagnostics;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace CompositorProbe;

public sealed record PresentSample(long PresentCount, uint PresentRefreshCount, uint SyncRefreshCount, long SyncQpc, int SourceIndex, double RenderMs);

public sealed record SeekResult(int TargetFrame, int PresentedFrame, int StampDecoded, bool StampMatches, double LatencyMs,
    double DecodeReadyMs, double PresentCallMs, int Slice);

public sealed record CompareFrame(int Frame, double T, RenderState State, byte[] PreviewPixels, byte[] SourcePixels, int Width, int Height, int SourceW, int SourceH);

public sealed class PlayStats
{
    public List<PresentSample> Samples { get; } = new();
    public int Presents, Repeats, SkippedSource, DecodeStarved, PresentFailures, StatsUnavailable, StatsDisjoint;
    public double DurationSec;
    public int FirstSource = -1, LastSource = -1;
    public double RenderMsMax, RenderMsSum;
}

/// <summary>
/// Decode pump thread + render thread around the compositor. The render thread is the only one that presents;
/// WM_SIZE resizes under host.RenderLock and calls RepaintLast so a resize never leaves an empty buffer on screen.
/// </summary>
public sealed class PreviewPlayer : IDisposable
{
    private readonly D3dHost _host;
    private readonly MfDecoder _decoder;
    private readonly VideoToBgra _vp;
    private readonly GpuReadback _readback;
    private readonly BlockingCollection<DecodedFrame> _queue = new(4);
    private readonly Thread _pump, _render;
    private readonly ManualResetEventSlim _pumpWake = new(false);
    private readonly BlockingCollection<Action> _commands = new();
    private volatile bool _stop;
    private int _seekFrame = -1;
    private long _epoch;
    private long _minEpoch;
    private DecodedFrame? _lookahead, _current;
    private int _syncInterval = 1;

    // Playback
    private volatile bool _playing;
    private long _playStartQpc;
    private PlayStats? _stats;
    private int _lastIndex = -1;
    private long _lastRecordedPresentCount = -1;
    public bool StripsEnabled { get; set; }
    public bool ZoomEnabled { get; set; } = true;
    private int _presentSeq;
    public (double setPositionMs, double firstFrameMs, double totalMs, int frames) LastSeekTiming => (_decoder.LastSetPositionMs, _decoder.LastFirstFrameMs, _decoder.LastDecodeMs, _decoder.LastDecodedFrames);
    public int Width => _decoder.Width;
    public int Height => _decoder.Height;
    public int SyncInterval { get => _syncInterval; set => _syncInterval = value; }
    public Exception? RenderError { get; private set; }

    public PreviewPlayer(D3dHost host, string clipPath)
    {
        _host = host;
        _decoder = new MfDecoder(host.Device, clipPath);
        _vp = new VideoToBgra(host.Device, host.Context, _decoder.Width, _decoder.Height);
        _readback = new GpuReadback(host.Device, host.Context);
        _host.Resized = RepaintLast;
        _pump = new Thread(PumpLoop) { IsBackground = true, Name = "decode-pump" };
        _render = new Thread(RenderLoop) { IsBackground = true, Name = "render" };
        _pump.Start(); _render.Start();
    }

    private static double Ms(long qpcDelta) => qpcDelta * 1000.0 / Stopwatch.Frequency;

    // ---------------- decode pump ----------------
    private void PumpLoop()
    {
        while (!_stop)
        {
            try
            {
                int req = Interlocked.Exchange(ref _seekFrame, -1);
                if (req >= 0)
                {
                    long epoch = Interlocked.Increment(ref _epoch);
                    while (_queue.TryTake(out var old)) old.Dispose();
                    var f = _decoder.SeekTo(req, epoch);
                    if (f != null && !TryEnqueue(f)) f.Dispose();
                    continue;
                }
                if (!_playing && _queue.Count >= 1) { _pumpWake.Wait(2); _pumpWake.Reset(); continue; }
                var frame = _decoder.ReadNext(Interlocked.Read(ref _epoch));
                if (frame == null) { Thread.Sleep(5); continue; }
                if (!TryEnqueue(frame)) frame.Dispose();
            }
            catch (Exception ex) { RenderError ??= ex; return; }
        }
    }

    private bool TryEnqueue(DecodedFrame f)
    {
        while (!_stop)
        {
            if (Volatile.Read(ref _seekFrame) >= 0) return false;
            if (_queue.TryAdd(f, 5)) return true;
        }
        return false;
    }

    // ---------------- render thread ----------------
    private void RenderLoop()
    {
        Native.timeBeginPeriod(1);
        try
        {
            while (!_stop)
            {
                while (_commands.TryTake(out var cmd)) cmd();
                if (_playing) PlayTick();
                else if (_commands.TryTake(out var c2, 3)) c2();
            }
        }
        catch (Exception ex) { RenderError ??= ex; }
        finally { Native.timeEndPeriod(1); }
    }

    private DecodedFrame? TakeFresh(long minEpoch)
    {
        while (true)
        {
            var f = _lookahead;
            _lookahead = null;
            if (f == null && !_queue.TryTake(out f)) return null;
            if (f.SeekId >= minEpoch) return f;
            f.Dispose();
        }
    }

    private void PlayTick()
    {
        long epoch = _minEpoch;
        var st = _stats;
        if (_playStartQpc == 0)
        {
            // Start the clock once the first frame and a lookahead are decoded.
            if (_current == null || _current.SeekId < epoch) { var first = TakeFresh(epoch); if (first == null) { Thread.Sleep(1); return; } SetCurrent(first); }
            if (_queue.Count < 2) { Thread.Sleep(1); return; }
            _playStartQpc = Stopwatch.GetTimestamp();
        }
        if (Native.WaitForSingleObject(_host.FrameLatencyHandle, 100) != 0) return;
        double elapsed = (Stopwatch.GetTimestamp() - _playStartQpc) / (double)Stopwatch.Frequency;
        int wanted = (int)Math.Floor(elapsed * TestClipWriter.Fps + 0.5);
        DecodedFrame? chosen = null;
        bool starved = false;
        while (true)
        {
            var f = TakeFresh(epoch);
            if (f == null) { starved = chosen == null && _current != null && _current.Index < wanted; break; }
            if (f.Index <= wanted) { chosen?.Dispose(); chosen = f; }
            else { _lookahead = f; break; }
        }
        bool repeat = chosen == null;
        if (!repeat) SetCurrent(chosen!);
        if (_current == null) return;
        PresentCurrent(st, repeat, starved);
    }

    private void SetCurrent(DecodedFrame f)
    {
        _current?.Dispose();
        _current = f;
        _vp.Convert(f);
    }

    private (long presentCount, double renderMs) RenderAndPresent(bool readback, int? syncInterval = null)
    {
        long t0 = Stopwatch.GetTimestamp();
        var f = _current!;
        var state = new RenderState(ZoomEnabled, StripsEnabled ? (_presentSeq++ & 0xFFFF) : -1);
        using var back = _host.SwapChain.GetBuffer<ID3D11Texture2D>(0);
        _host.Compositor.Render(state, f.PtsHns / 1e7, _vp.Output, back);
        if (readback) _readback.Enqueue(back);
        var hr = _host.SwapChain.Present((uint)(syncInterval ?? _syncInterval), PresentFlags.None);
        if (hr.Failure) throw new InvalidOperationException("Present failed " + hr);
        double ms = Ms(Stopwatch.GetTimestamp() - t0);
        return (_host.SwapChain.LastPresentCount, ms);
    }

    private void PresentCurrent(PlayStats? st, bool repeat, bool starved)
    {
        long pc; double ms;
        lock (_host.RenderLock)
        {
            try { (pc, ms) = RenderAndPresent(false); }
            catch (Exception ex) { if (st != null) st.PresentFailures++; RenderError ??= ex; return; }
            if (st == null) return;
            st.Presents++;
            st.RenderMsSum += ms; st.RenderMsMax = Math.Max(st.RenderMsMax, ms);
            int idx = _current!.Index;
            if (repeat) { st.Repeats++; if (starved) st.DecodeStarved++; }
            else if (_lastIndex >= 0 && idx - _lastIndex > 1) st.SkippedSource += idx - _lastIndex - 1;
            if (st.FirstSource < 0) st.FirstSource = idx;
            st.LastSource = idx; _lastIndex = idx;
            CollectStats(st, idx, ms);
        }
    }

    private void CollectStats(PlayStats st, int srcIdx, double ms)
    {
        var hr = _host.SwapChain.GetFrameStatistics(out FrameStatistics fs);
        if (hr.Failure)
        {
            if ((uint)hr.Code == 0x887A000B) st.StatsDisjoint++; else st.StatsUnavailable++;
            return;
        }
        if (fs.PresentCount == _lastRecordedPresentCount) return;
        _lastRecordedPresentCount = fs.PresentCount;
        st.Samples.Add(new PresentSample(fs.PresentCount, fs.PresentRefreshCount, fs.SyncRefreshCount, fs.SyncQPCTime, srcIdx, ms));
    }

    /// <summary>Repaint the current frame on the UI thread inside WM_SIZE (RenderLock already held).</summary>
    private void RepaintLast()
    {
        if (_current == null) return;
        try { RenderAndPresent(false); } catch (Exception ex) { RenderError ??= ex; }
    }

    // ---------------- public API ----------------
    private Task<T> OnRenderThread<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _commands.Add(() => { try { tcs.SetResult(work()); } catch (Exception ex) { tcs.SetException(ex); } });
        return tcs.Task;
    }

    private void RequestSeek(int frame)
    {
        Interlocked.Exchange(ref _seekFrame, frame);
        _pumpWake.Set();
    }

    /// <summary>
    /// Seek and measure: t0 is taken at the API call; the end time is SyncQPCTime of the present that carries the
    /// target frame (GetFrameStatistics). The stamp is decoded from the back buffer copy taken just before Present.
    /// </summary>
    public Task<SeekResult> SeekAsync(int frame, int? syncInterval = null)
    {
        long t0 = Stopwatch.GetTimestamp();
        long expectEpoch = Interlocked.Read(ref _epoch) + 1;
        RequestSeek(frame);
        return OnRenderThread(() =>
        {
            DecodedFrame? f = null;
            long deadline = Stopwatch.GetTimestamp() + 2 * Stopwatch.Frequency;
            while (f == null && Stopwatch.GetTimestamp() < deadline)
            {
                var c = TakeFresh(expectEpoch);
                if (c != null && c.Index == frame) f = c;
                else if (c != null) c.Dispose();
                else Thread.Sleep(0);
            }
            if (f == null) throw new TimeoutException("seek frame never decoded: " + frame);
            long tReady = Stopwatch.GetTimestamp();
            long pc; int stamp; int slice = (int)f.Slice;
            double presentCallMs;
            byte[] pixels;
            lock (_host.RenderLock)
            {
                SetCurrent(f);
                (pc, _) = RenderAndPresent(true, syncInterval);
                presentCallMs = Ms(Stopwatch.GetTimestamp() - t0);
                pixels = _readback.Fetch();
            }
            var back = (w: _host.BufferWidth, h: _host.BufferHeight);
            var state = new RenderState(ZoomEnabled, -1);
            stamp = StampFromCanvas(pixels, back.w, back.h, state, f.PtsHns / 1e7);
            long sync = WaitSync(pc);
            return new SeekResult(frame, f.Index, stamp, stamp == frame, Ms(sync - t0), Ms(tReady - t0), presentCallMs, slice);
        });
    }

    /// <summary>
    /// Measures the display refresh rate the swapchain actually runs at: presents the current frame with sync interval 1
    /// and divides the PresentRefreshCount delta by the SyncQPCTime delta (GetFrameStatistics) between two presents.
    /// </summary>
    public Task<(double hz, long refreshes, double seconds, int presents)> MeasureRefreshAsync(int presents)
    {
        return OnRenderThread(() =>
        {
            FrameStatistics first = default, last = default;
            long firstPc = 0, lastPc = 0;
            for (int i = 0; i < presents; i++)
            {
                Native.WaitForSingleObject(_host.FrameLatencyHandle, 100);
                long pc;
                lock (_host.RenderLock)
                {
                    var f = _current!;
                    using var back = _host.SwapChain.GetBuffer<ID3D11Texture2D>(0);
                    _host.Compositor.Render(new RenderState(false, -1), f.PtsHns / 1e7, _vp.Output, back);
                    var hr = _host.SwapChain.Present(1, PresentFlags.None);
                    if (hr.Failure) throw new InvalidOperationException("Present failed " + hr);
                    pc = _host.SwapChain.LastPresentCount;
                    if (i >= 5) // skip the first presents: they ramp up from the previous cadence
                    {
                        if (firstPc == 0 && _host.SwapChain.GetFrameStatistics(out var s).Success && s.PresentCount > 0) { first = s; firstPc = s.PresentCount; }
                    }
                }
                if (i >= 5 && firstPc != 0 && lastPc < pc - 3)
                {
                    lock (_host.RenderLock) { if (_host.SwapChain.GetFrameStatistics(out var s).Success && s.PresentCount > lastPc) { last = s; lastPc = s.PresentCount; } }
                }
            }
            if (firstPc == 0 || lastPc <= firstPc) return (double.NaN, 0L, 0.0, presents);
            double sec = (last.SyncQPCTime - first.SyncQPCTime) / (double)Stopwatch.Frequency;
            long rc = (long)last.SyncRefreshCount - first.SyncRefreshCount;
            return (rc / sec, rc, sec, presents);
        });
    }

    private long WaitSync(long presentCount)
    {
        long deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
        while (Stopwatch.GetTimestamp() < deadline)
        {
            FrameStatistics fs;
            SharpGen.Runtime.Result hr;
            lock (_host.RenderLock) hr = _host.SwapChain.GetFrameStatistics(out fs);
            if (hr.Success && fs.PresentCount >= presentCount) return fs.SyncQPCTime;
            Thread.Sleep(1);
        }
        throw new TimeoutException("no frame statistics for present " + presentCount);
    }

    private int StampFromCanvas(byte[] bgra, int w, int h, RenderState state, double t)
    {
        var m = ProbeCompositor.SourceToCanvas(state, t, _decoder.Width, _decoder.Height, w, h);
        return FrameStamp.Decode(bit =>
        {
            var (sx, sy) = FrameStamp.SourceCellCenter(bit);
            var p = System.Numerics.Vector2.Transform(new System.Numerics.Vector2(sx, sy), m);
            int x = (int)Math.Round(p.X), y = (int)Math.Round(p.Y);
            if (x < 0 || y < 0 || x >= w || y >= h) return false;
            return bgra[(y * w + x) * 4 + 1] > 128;
        }, FrameStamp.Bits);
    }

    /// <summary>Seek to a frame, render it through the live swapchain and return the presented pixels plus the BGRA source.</summary>
    public Task<CompareFrame> CaptureFrameAsync(int frame, bool strips)
    {
        long expectEpoch = Interlocked.Read(ref _epoch) + 1;
        RequestSeek(frame);
        return OnRenderThread(() =>
        {
            DecodedFrame? f = null;
            long deadline = Stopwatch.GetTimestamp() + 3 * Stopwatch.Frequency;
            while (f == null && Stopwatch.GetTimestamp() < deadline)
            {
                var c = TakeFresh(expectEpoch);
                if (c != null && c.Index == frame) f = c; else if (c != null) c.Dispose(); else Thread.Sleep(1);
            }
            if (f == null) throw new TimeoutException("frame not decoded: " + frame);
            lock (_host.RenderLock)
            {
                SetCurrent(f);
                var state = new RenderState(ZoomEnabled, strips ? (frame & 0xFFFF) : -1);
                using var back = _host.SwapChain.GetBuffer<ID3D11Texture2D>(0);
                _host.Compositor.Render(state, f.PtsHns / 1e7, _vp.Output, back);
                var preview = _readback.Read(back);
                var srcPixels = _readback.Read(_vp.Output);
                var hr = _host.SwapChain.Present((uint)_syncInterval, PresentFlags.None);
                if (hr.Failure) throw new InvalidOperationException("Present failed " + hr);
                return new CompareFrame(frame, f.PtsHns / 1e7, state, preview, srcPixels, _host.BufferWidth, _host.BufferHeight, _decoder.Width, _decoder.Height);
            }
        });
    }

    /// <summary>Plays from frame 0 for the given time, recording per-present statistics.</summary>
    public async Task<PlayStats> PlayAsync(double seconds, Action<double>? tick = null)
    {
        var stats = new PlayStats();
        await OnRenderThread(() =>
        {
            _lookahead?.Dispose(); _lookahead = null;
            _current?.Dispose(); _current = null;
            _playStartQpc = 0; _lastIndex = -1; _lastRecordedPresentCount = -1;
            _minEpoch = Interlocked.Read(ref _epoch) + 1;
            RequestSeek(0);
            _stats = stats;
            _playing = true;
            return 0;
        });
        long start = Stopwatch.GetTimestamp();
        while (true)
        {
            double el = (Stopwatch.GetTimestamp() - (_playStartQpc == 0 ? start : _playStartQpc)) / (double)Stopwatch.Frequency;
            if (_playStartQpc != 0 && el >= seconds) break;
            if (_playStartQpc == 0 && el > 10) throw new TimeoutException($"playback never started: current={(_current == null ? "null" : _current.SeekId.ToString())} queue={_queue.Count} epoch={Interlocked.Read(ref _epoch)} minEpoch={_minEpoch} seekPending={Volatile.Read(ref _seekFrame)} decoderEos={_decoder.EndOfStream} renderError={RenderError} waitHandle={Native.WaitForSingleObject(_host.FrameLatencyHandle, 0)}");
            tick?.Invoke(el);
            await Task.Delay(50);
        }
        _playing = false;
        stats.DurationSec = (Stopwatch.GetTimestamp() - _playStartQpc) / (double)Stopwatch.Frequency;
        // Let the last presents reach DWM, then collect their statistics.
        await Task.Delay(300);
        lock (_host.RenderLock) CollectStats(stats, stats.LastSource, 0);
        _stats = null;
        _playStartQpc = 0;
        return stats;
    }

    public int ReadSwapChainBackbufferCount() => 3;

    public void Dispose()
    {
        _stop = true;
        _pumpWake.Set();
        _pump.Join(2000); _render.Join(2000);
        while (_queue.TryTake(out var f)) f.Dispose();
        _lookahead?.Dispose(); _current?.Dispose();
        _readback.Dispose(); _vp.Dispose(); _decoder.Dispose();
    }
}
