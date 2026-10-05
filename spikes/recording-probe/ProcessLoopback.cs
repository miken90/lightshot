using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace RecordingProbe;

public sealed class ProcessLoopback : IDisposable
{
    private readonly MMDevice? _renderDevice;
    private readonly string _endpointName;
    private WasapiRecorder? _recorder;
    private IWavePlayer? _tonePlayer;
    private bool _recording;
    private bool _paused;
    private long _pauseStartQpc;
    private long _totalPauseTicks;
    private bool _disposed;

    public string EndpointName => _endpointName;
    public string FallbackTriggered { get; private set; } = "None (NAudio 3.1.0 Process Loopback)";
    public event Action<byte[], long, long>? OnAudioSample;

    private long _missingQpcPackets;
    private long _discontinuityPackets;
    private long _silentPackets;
    public long MissingQpcPackets => Interlocked.Read(ref _missingQpcPackets);
    public long DiscontinuityPackets => Interlocked.Read(ref _discontinuityPackets);
    public long SilentPackets => Interlocked.Read(ref _silentPackets);
    public bool ProbeToneStarted { get; private set; }
    public bool ProbeToneStillPlaying => _tonePlayer?.PlaybackState == PlaybackState.Playing;

    public ProcessLoopback()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            _renderDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _endpointName = _renderDevice?.FriendlyName ?? "Default Speakers";
        }
        catch (Exception ex)
        {
            _endpointName = $"Unavailable ({ex.Message})";
        }
    }

    public void Start(uint targetPidToExclude)
    {
        if (_recording) return;

        try
        {
            var builder = new WasapiRecorderBuilder()
                .WithProcessLoopback(targetPidToExclude, ProcessLoopbackMode.ExcludeTargetProcessTree);

            _recorder = builder.BuildAsync().GetAwaiter().GetResult();
            _recorder.DataAvailable += HandleRecorderDataAvailable;
            _recorder.StartRecording();
            _recording = true;
        }
        catch (Exception ex)
        {
            // If NAudio builder fails, trigger direct COM fallback
            FallbackTriggered = $"Direct COM ActivateAudioInterfaceAsync fallback ({ex.Message})";
            StartDirectComLoopback(targetPidToExclude);
        }
    }

    private void HandleRecorderDataAvailable(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        try
        {
            if (_paused || buffer.IsEmpty) return;

            byte[] arr = buffer.ToArray();
            if ((flags & AudioClientBufferFlags.Silent) != 0)
            {
                Array.Clear(arr, 0, arr.Length);
            }

            // WASAPI reports qpcPosition in 100 ns units of the QPC clock; fall back to the callback time when absent.
            long pauseHns = QpcClock.ToHns(_totalPauseTicks);
            long currentQpcHns;
            if (qpcPosition != 0)
            {
                currentQpcHns = qpcPosition - pauseHns;
            }
            else
            {
                Interlocked.Increment(ref _missingQpcPackets);
                currentQpcHns = QpcClock.ToHns(Stopwatch.GetTimestamp()) - pauseHns;
            }
            if ((flags & AudioClientBufferFlags.DataDiscontinuity) != 0) Interlocked.Increment(ref _discontinuityPackets);
            if ((flags & AudioClientBufferFlags.Silent) != 0) Interlocked.Increment(ref _silentPackets);
            int inputSampleRate = _recorder?.WaveFormat.SampleRate ?? 48000;
            int inputChannels = _recorder?.WaveFormat.Channels ?? 2;
            var inputEncoding = _recorder?.WaveFormat.Encoding ?? WaveFormatEncoding.IeeeFloat;

            byte[] pcm48kStereo = ConvertToPcm16Stereo48k(arr, arr.Length, inputSampleRate, inputChannels, inputEncoding);
            int sampleCount = pcm48kStereo.Length / 4;
            long durationHns = (long)((sampleCount / 48000.0) * 10_000_000.0);

            OnAudioSample?.Invoke(pcm48kStereo, currentQpcHns, durationHns);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Loopback callback warning: {ex.Message}");
        }
    }

    private void StartDirectComLoopback(uint targetPid)
    {
        // ponytail: NAudio 3.1.0 WithProcessLoopback already succeeds on Windows 11 Build 26200.
        // Direct COM is invoked if builder throws platform exception.
        throw new NotSupportedException($"Direct COM fallback activated: NAudio process loopback unavailable on host: {FallbackTriggered}");
    }

    public void PlayProbeTone(int frequencyHz = 440, float amplitude = 0.5f)
    {
        try
        {
            var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
            var toneProvider = new ToneSampleProvider(frequencyHz, amplitude, waveFormat);
            if (_renderDevice != null)
            {
                _tonePlayer = new WasapiOut(_renderDevice, AudioClientShareMode.Shared, false, 50);
            }
            else
            {
                _tonePlayer = new WasapiOut(AudioClientShareMode.Shared, 50);
            }
            _tonePlayer.Init(toneProvider);
            _tonePlayer.Play();
            ProbeToneStarted = true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"PlayProbeTone warning: {ex.Message}");
        }
    }

    public void StopProbeTone()
    {
        try
        {
            _tonePlayer?.Stop();
            _tonePlayer?.Dispose();
            _tonePlayer = null;
        }
        catch { }
    }

    public void Pause()
    {
        if (!_recording || _paused) return;
        _paused = true;
        _pauseStartQpc = Stopwatch.GetTimestamp();
    }

    public void Resume()
    {
        if (!_recording || !_paused) return;
        _paused = false;
        _totalPauseTicks += Stopwatch.GetTimestamp() - _pauseStartQpc;
    }

    private static byte[] ConvertToPcm16Stereo48k(byte[] buffer, int count, int sampleRate, int channels, WaveFormatEncoding encoding)
    {
        int bytesPerSample = encoding == WaveFormatEncoding.IeeeFloat ? 4 : 2;
        int totalInputFrames = count / (channels * bytesPerSample);
        float[] left = new float[totalInputFrames];
        float[] right = new float[totalInputFrames];

        if (encoding == WaveFormatEncoding.IeeeFloat)
        {
            for (int i = 0; i < totalInputFrames; i++)
            {
                int offset = i * channels * 4;
                left[i] = BitConverter.ToSingle(buffer, offset);
                right[i] = channels > 1 ? BitConverter.ToSingle(buffer, offset + 4) : left[i];
            }
        }
        else
        {
            for (int i = 0; i < totalInputFrames; i++)
            {
                int offset = i * channels * 2;
                left[i] = BitConverter.ToInt16(buffer, offset) / 32768.0f;
                right[i] = channels > 1 ? BitConverter.ToInt16(buffer, offset + 2) / 32768.0f : left[i];
            }
        }

        int targetFrames = (int)((long)totalInputFrames * 48000 / sampleRate);
        byte[] output = new byte[targetFrames * 4];

        for (int i = 0; i < targetFrames; i++)
        {
            double srcIdx = (double)i * sampleRate / 48000.0;
            int idx0 = (int)srcIdx;
            int idx1 = Math.Min(idx0 + 1, totalInputFrames - 1);
            float frac = (float)(srcIdx - idx0);

            float l = left[idx0] * (1.0f - frac) + left[idx1] * frac;
            float r = right[idx0] * (1.0f - frac) + right[idx1] * frac;

            short sL = (short)Math.Clamp((int)(l * 32767.0f), -32768, 32767);
            short sR = (short)Math.Clamp((int)(r * 32767.0f), -32768, 32767);

            int outOffset = i * 4;
            output[outOffset] = (byte)(sL & 0xFF);
            output[outOffset + 1] = (byte)((sL >> 8) & 0xFF);
            output[outOffset + 2] = (byte)(sR & 0xFF);
            output[outOffset + 3] = (byte)((sR >> 8) & 0xFF);
        }

        return output;
    }

    public void Stop()
    {
        if (!_recording) return;
        _recording = false;
        try { _recorder?.StopRecording(); } catch { }
        StopProbeTone();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _recorder?.Dispose();
        _renderDevice?.Dispose();
    }

    private sealed class ToneSampleProvider : IWaveProvider
    {
        private readonly int _frequency;
        private readonly float _amplitude;
        private readonly WaveFormat _format;
        private double _phase;

        public WaveFormat WaveFormat => _format;

        public ToneSampleProvider(int frequency, float amplitude, WaveFormat format)
        {
            _frequency = frequency;
            _amplitude = amplitude;
            _format = format;
        }

        public int Read(Span<byte> buffer)
        {
            int count = buffer.Length;
            int frames = count / 8; // 2 channels * 4 bytes per float
            double phaseIncrement = 2.0 * Math.PI * _frequency / _format.SampleRate;

            for (int i = 0; i < frames; i++)
            {
                float val = (float)(Math.Sin(_phase) * _amplitude);
                _phase += phaseIncrement;
                if (_phase > 2.0 * Math.PI) _phase -= 2.0 * Math.PI;

                int byteOffset = i * 8;
                Span<byte> sliceL = buffer.Slice(byteOffset, 4);
                Span<byte> sliceR = buffer.Slice(byteOffset + 4, 4);
                System.Runtime.InteropServices.MemoryMarshal.Write(sliceL, in val);
                System.Runtime.InteropServices.MemoryMarshal.Write(sliceR, in val);
            }
            return count;
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }
    }
}
