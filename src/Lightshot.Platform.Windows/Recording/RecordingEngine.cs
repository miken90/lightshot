// Ported for Lightshot Windows Port (Phase 7 R2)
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lightshot.Core;
using Lightshot.Platform.Windows.Audio;
using Lightshot.Platform.Windows.DesktopCover;
using Lightshot.Platform.Windows.Displays;
using Lightshot.Platform.Windows.Input;
using Vortice.Direct3D11;
using CoreVideoCodec = Lightshot.Core.VideoCodec;
using EncoderVideoCodec = Lightshot.Platform.Windows.Recording.VideoCodec;
using Rect = Lightshot.Core.Rect;

namespace Lightshot.Platform.Windows.Recording;

/// <summary>
/// Core recording engine coordinating screen capture, compositor overlays, video processing pipeline,
/// frame cadence driver, fragmented MP4 sink writer, power assertions, audio pump, and finalization.
/// </summary>
public sealed class RecordingEngine : IDisposable
{
    private readonly ScratchStore _scratchStore;
    private readonly EncoderSelector _encoderSelector;
    private readonly TakeFinalizer _takeFinalizer;
    private readonly Func<IReadOnlyList<DisplayInfo>> _displayProvider;
    private readonly IInputEventSource? _inputEventSource;
    private readonly AudioDeviceService _audioDeviceService;

    private readonly object _syncLock = new();

    private DdaFrameSource? _frameSource;
    private VideoProcessorPipeline? _pipeline;
    private BurnInCompositor? _compositor;
    private ID3D11Texture2D? _intermediateBgra;
    private MfFragmentedWriter? _writer;
    private CadenceDriver<ID3D11Texture2D>? _cadenceDriver;
    private PauseClock? _pauseClock;
    private PowerRequest? _powerRequest;

    private DesktopCover.DesktopCover? _desktopCover;
    private RecordingInputSession? _inputSession;
    private RecordingAudioSession? _audioSession;

    private Thread? _captureThread;
    private CancellationTokenSource? _cts;
    private Action<RecordingEvent>? _onEvent;

    private RecordingOptions? _activeOptions;
    private Rect _cropRect;
    private string? _takePath;
    private string? _destinationPath;
    private long _startQpc;
    private bool _isRecording;
    private bool _disposed;

    static RecordingEngine()
    {
        try
        {
            Vortice.MediaFoundation.MediaFactory.MFStartup();
        }
        catch { }
    }

    public bool IsRecording => _isRecording;
    public bool IsPaused => _pauseClock?.IsPaused ?? false;
    public RecordingOptions? ActiveOptions => _activeOptions;
    public string? DestinationPath => _destinationPath;
    public AudioDeviceService AudioDeviceService => _audioDeviceService;

    public TimeSpan Duration
    {
        get
        {
            if (!_isRecording || _pauseClock == null) return TimeSpan.Zero;
            long rawElapsedHns = QpcClock.ToHns(QpcClock.NowTicks - _startQpc);
            long adjustedHns = _pauseClock.AdjustTimestamp(rawElapsedHns);
            return TimeSpan.FromTicks(Math.Max(0, adjustedHns));
        }
    }

    public RecordingEngine(
        ScratchStore? scratchStore = null,
        EncoderSelector? encoderSelector = null,
        TakeFinalizer? takeFinalizer = null,
        Func<IReadOnlyList<DisplayInfo>>? displayProvider = null,
        IInputEventSource? inputEventSource = null,
        AudioDeviceService? audioDeviceService = null)
    {
        _scratchStore = scratchStore ?? new ScratchStore();
        _encoderSelector = encoderSelector ?? new EncoderSelector();
        _takeFinalizer = takeFinalizer ?? new TakeFinalizer(new Mp4Remuxer());
        _displayProvider = displayProvider ?? (() => DisplayTopology.GetDisplays());
        _inputEventSource = inputEventSource;
        _audioDeviceService = audioDeviceService ?? new AudioDeviceService();
    }

    /// <summary>
    /// Starts recording with the given options to the specified output path.
    /// </summary>
    public Task<RecordingError?> StartAsync(RecordingOptions options, string outputPath, Action<RecordingEvent>? onEvent = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(options);

        lock (_syncLock)
        {
            if (_isRecording)
            {
                return Task.FromResult<RecordingError?>(new RecordingError.SystemFailure("Recording is already in progress."));
            }

            var displays = _displayProvider();
            if (displays.Count == 0)
            {
                return Task.FromResult<RecordingError?>(new RecordingError.NoDisplayAvailable());
            }

            MaxResolution maxRes = MaxResolution.Original;
            int fps = 30;
            bool useHevc = false;

            if (options.Output is RecordingOutput.Video v)
            {
                fps = v.Settings.Fps;
                useHevc = v.Settings.Codec == CoreVideoCodec.Hevc;
                maxRes = v.Settings.MaxResolution;
            }
            else if (options.Output is RecordingOutput.Gif g)
            {
                fps = g.Settings.Fps;
            }
            if (fps <= 0) fps = 30;

            var resolved = RecordingDisplayResolver.Resolve(displays, options.Region, maxRes);
            var targetDisplay = resolved.Display;
            _cropRect = resolved.LocalRect;

            int cropW = Math.Max(2, ((int)Math.Round(_cropRect.Width) / 2) * 2);
            int cropH = Math.Max(2, ((int)Math.Round(_cropRect.Height) / 2) * 2);

            int outputW = Math.Max(2, (resolved.OutputWidth / 2) * 2);
            int outputH = Math.Max(2, (resolved.OutputHeight / 2) * 2);

            _destinationPath = string.IsNullOrWhiteSpace(outputPath)
                ? _scratchStore.GetFinalMp4Path(_scratchStore.CreateTakePath("take"))
                : outputPath;
            _takePath = _scratchStore.CreateTakePath("take");

            try
            {
                if (options.HideDesktopIcons)
                {
                    _desktopCover = DesktopCover.DesktopCover.Show();
                }

                _frameSource = new DdaFrameSource(targetDisplay);
                _pipeline = new VideoProcessorPipeline(_frameSource.Device, _frameSource.Context, cropW, cropH, outputW, outputH, fps);

                if (options.HighlightClicks || options.ShowKeystrokes)
                {
                    _inputSession = new RecordingInputSession(options, _inputEventSource);
                    _compositor = new BurnInCompositor(options, new Point(_cropRect.X, _cropRect.Y), targetDisplay.ScaleFactor, _inputSession.EffectiveSource);
                    _intermediateBgra = _pipeline.CreateBgraIntermediateTexture();
                }

                _audioSession = new RecordingAudioSession(_audioDeviceService, options, WriteAudioSample);

                var selectedEncoder = _encoderSelector.SelectEncoder(preferHevc: useHevc);
                _writer = new MfFragmentedWriter(
                    _takePath,
                    _frameSource.Device,
                    outputW,
                    outputH,
                    fps,
                    audioTrackCount: _audioSession.TrackConfigs.Count,
                    useHevc: useHevc && selectedEncoder.Codec == EncoderVideoCodec.Hevc,
                    forceSoftware: !selectedEncoder.IsHardware,
                    audioTrackConfigs: _audioSession.TrackConfigs);

                _cadenceDriver = new CadenceDriver<ID3D11Texture2D>(fps);
                _pauseClock = new PauseClock();
                _powerRequest = new PowerRequest("Lightshot Screen Recording");
                _powerRequest.Activate();

                _activeOptions = options;
                _onEvent = onEvent;
                _cts = new CancellationTokenSource();
                _isRecording = true;
                _startQpc = QpcClock.NowTicks;

                RecordingSounds.Play(RecordingCue.Start, true);

                _captureThread = new Thread(CaptureLoop)
                {
                    IsBackground = true,
                    Name = "LightshotCaptureLoop"
                };
                _captureThread.Start();

                _audioSession.Start();

                return Task.FromResult<RecordingError?>(null);
            }
            catch (Exception ex)
            {
                CleanupResources();
                return Task.FromResult<RecordingError?>(new RecordingError.SystemFailure($"Failed to initialize recording: {ex.Message}"));
            }
        }
    }

    public void WriteAudioSample(int trackIndex, byte[] pcmData, long sampleTimeHns, long durationHns)
    {
        if (!_isRecording || _writer == null) return;
        int streamIndex = trackIndex;
        if (!_writer.AudioStreamIndices.Contains(trackIndex))
        {
            if (trackIndex >= 0 && trackIndex < _writer.AudioStreamIndices.Count)
            {
                streamIndex = _writer.AudioStreamIndices[trackIndex];
            }
            else
            {
                return;
            }
        }
        _writer.WriteAudioSample(streamIndex, pcmData, sampleTimeHns, durationHns);
    }

    private void CaptureLoop()
    {
        if (_frameSource == null || _pipeline == null || _writer == null || _cadenceDriver == null || _pauseClock == null || _cts == null)
        {
            return;
        }

        int fps = _cadenceDriver.Fps;
        int targetIntervalMs = Math.Max(1, 1000 / fps);
        int timeoutMs = Math.Max(5, targetIntervalMs / 2);
        long startQpc = _startQpc;

        while (!_cts.Token.IsCancellationRequested && _isRecording)
        {
            if (_pauseClock.IsPaused)
            {
                Thread.Sleep(10);
                continue;
            }

            long nowQpc = QpcClock.NowTicks;

            var status = _frameSource.AcquireFrame(timeoutMs, out var desktopTexture, out var errorMessage);

            if (status == FrameAcquireStatus.ResolutionChanged)
            {
                _onEvent?.Invoke(new RecordingEvent.Failed(new RecordingError.SystemFailure(errorMessage ?? "Display resolution changed during recording.")));
                _isRecording = false;
                break;
            }

            if (desktopTexture != null)
            {
                _cadenceDriver.SubmitFrame(desktopTexture);
            }

            long rawElapsedHns = QpcClock.ToHns(nowQpc - startQpc);
            long adjustedElapsedHns = _pauseClock.AdjustTimestamp(rawElapsedHns);

            _cadenceDriver.EmitDueFrames(adjustedElapsedHns, (frame, sampleTimeHns, durationHns, isRepeat) =>
            {
                try
                {
                    if (_compositor != null && _intermediateBgra != null)
                    {
                        var srcBox = new Vortice.Mathematics.Box(
                            (int)_cropRect.X,
                            (int)_cropRect.Y,
                            0,
                            (int)(_cropRect.X + _cropRect.Width),
                            (int)(_cropRect.Y + _cropRect.Height),
                            1);

                        _frameSource.Context.CopySubresourceRegion(
                            _intermediateBgra, 0, 0, 0, 0,
                            frame, 0, srcBox);

                        double frameTimeSec = (double)sampleTimeHns / 10_000_000.0;
                        _compositor.Composite(_intermediateBgra, frameTimeSec);
                        _pipeline.Process(_intermediateBgra);
                    }
                    else
                    {
                        _pipeline.Process(frame, _cropRect);
                    }

                    _writer.WriteVideoFrame(_pipeline.OutputTexture, sampleTimeHns, durationHns);
                }
                catch (Exception ex)
                {
                    _onEvent?.Invoke(new RecordingEvent.Failed(new RecordingError.SystemFailure($"Error encoding frame: {ex.Message}")));
                }
            });

            Thread.Sleep(1);
        }
    }

    public Task PauseAsync()
    {
        lock (_syncLock)
        {
            if (!_isRecording || _pauseClock == null || _pauseClock.IsPaused)
            {
                return Task.CompletedTask;
            }

            long nowHns = QpcClock.ToHns(QpcClock.NowTicks - _startQpc);
            _pauseClock.Pause(nowHns);
            _audioSession?.Pause();
            RecordingSounds.Play(RecordingCue.Pause, true);
        }
        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        lock (_syncLock)
        {
            if (!_isRecording || _pauseClock == null || !_pauseClock.IsPaused)
            {
                return Task.CompletedTask;
            }

            long nowHns = QpcClock.ToHns(QpcClock.NowTicks - _startQpc);
            _pauseClock.Resume(nowHns);
            _audioSession?.Resume();
        }
        return Task.CompletedTask;
    }

    public Task<(string? Path, RecordingError? Error)> StopAsync()
    {
        lock (_syncLock)
        {
            if (!_isRecording || _writer == null || _takePath == null || _destinationPath == null)
            {
                return Task.FromResult<(string? Path, RecordingError? Error)>((null, new RecordingError.SystemFailure("Recording is not active.")));
            }

            // Stop timestamp taken before stopping capture per spec
            long stopQpc = QpcClock.NowTicks;
            _isRecording = false;

            _cts?.Cancel();
            try { _captureThread?.Join(3000); } catch { }
            _audioSession?.Stop();

            // Flush final cadence frames up to stop timestamp
            if (_cadenceDriver != null && _pauseClock != null && _pipeline != null && _frameSource != null)
            {
                long finalElapsedHns = _pauseClock.AdjustTimestamp(QpcClock.ToHns(stopQpc - _startQpc));
                _cadenceDriver.EmitDueFrames(finalElapsedHns, (frame, sampleTimeHns, durationHns, isRepeat) =>
                {
                    try
                    {
                        _pipeline.Process(frame, _cropRect);
                        _writer.WriteVideoFrame(_pipeline.OutputTexture, sampleTimeHns, durationHns);
                    }
                    catch { }
                });
            }

            try
            {
                _writer.FinalizeWriting(stripMfra: true);
            }
            catch (Exception ex)
            {
                CleanupResources();
                return Task.FromResult<(string? Path, RecordingError? Error)>((null, new RecordingError.SystemFailure($"Finalizing fragmented writer failed: {ex.Message}")));
            }

            RecordingSounds.Play(RecordingCue.Stop, true);
            _powerRequest?.Deactivate();

            string takePath = _takePath;
            string destPath = _destinationPath;

            CleanupResources();

            var finalizeResult = _takeFinalizer.FinalizeTake(takePath, destPath);
            if (finalizeResult.Success && File.Exists(destPath))
            {
                try { File.Delete(takePath); } catch { }
                return Task.FromResult<(string? Path, RecordingError? Error)>((destPath, null));
            }

            return Task.FromResult<(string? Path, RecordingError? Error)>((null, new RecordingError.SystemFailure(finalizeResult.ErrorMessage ?? "Remuxing and finalizing take failed.")));
        }
    }

    public Task CancelAsync()
    {
        lock (_syncLock)
        {
            if (!_isRecording) return Task.CompletedTask;
            _isRecording = false;

            _cts?.Cancel();
            try { _captureThread?.Join(1000); } catch { }

            string? takePath = _takePath;
            CleanupResources();

            if (takePath != null && File.Exists(takePath))
            {
                try { File.Delete(takePath); } catch { }
            }
        }
        return Task.CompletedTask;
    }

    private void CleanupResources()
    {
        _cts?.Dispose();
        _cts = null;
        _captureThread = null;

        _audioSession?.Dispose();
        _audioSession = null;

        _inputSession?.Dispose();
        _inputSession = null;

        _desktopCover?.Dispose();
        _desktopCover = null;

        _writer?.Dispose();
        _writer = null;

        _intermediateBgra?.Dispose();
        _intermediateBgra = null;

        _compositor?.Dispose();
        _compositor = null;

        _pipeline?.Dispose();
        _pipeline = null;

        _frameSource?.Dispose();
        _frameSource = null;

        _powerRequest?.Deactivate();
        _powerRequest?.Dispose();
        _powerRequest = null;

        _isRecording = false;
        _activeOptions = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        CancelAsync().GetAwaiter().GetResult();
    }
}
