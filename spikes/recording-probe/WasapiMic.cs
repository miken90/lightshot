using System;
using System.Diagnostics;
using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace RecordingProbe;

public sealed class WasapiMic : IDisposable
{
    private readonly MMDevice? _device;
    private WasapiCapture? _capture;
    private readonly string _endpointName;
    private bool _recording;
    private bool _paused;
    private long _pauseStartQpc;
    private long _totalPauseTicks;
    private bool _disposed;

    public string EndpointName => _endpointName;
    public event Action<byte[], long, long>? OnAudioSample;

    public WasapiMic()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            _device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
            _endpointName = _device?.FriendlyName ?? "Default Microphone";
        }
        catch (Exception ex)
        {
            _endpointName = $"Unavailable ({ex.Message})";
        }
    }

    public void Start()
    {
        if (_recording || _device == null) return;

        _capture = new WasapiCapture(_device);
        _capture.DataAvailable += HandleDataAvailable;
        _capture.StartRecording();
        _recording = true;
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

    private void HandleDataAvailable(object? sender, WaveInEventArgs e)
    {
        try
        {
            if (_paused || e.BytesRecorded == 0) return;

            int inputSampleRate = _capture?.WaveFormat.SampleRate ?? 48000;
            int inputChannels = _capture?.WaveFormat.Channels ?? 2;
            var inputEncoding = _capture?.WaveFormat.Encoding ?? WaveFormatEncoding.IeeeFloat;

            // Convert audio buffer to 16-bit PCM 48kHz stereo
            byte[] pcm48kStereo = ConvertToPcm16Stereo48k(e.Buffer, e.BytesRecorded, inputSampleRate, inputChannels, inputEncoding);

            // Calculate sample duration in HNS
            int sampleCount = pcm48kStereo.Length / 4;
            long durationHns = (long)((sampleCount / 48000.0) * 10_000_000.0);

            // Timestamp in QPC 100 ns units (pause offset removed): the packet ends about now, so it started one duration ago.
            long nowHns = QpcClock.ToHns(Stopwatch.GetTimestamp() - _totalPauseTicks);
            OnAudioSample?.Invoke(pcm48kStereo, nowHns - durationHns, durationHns);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"WasapiMic callback warning: {ex.Message}");
        }
    }

    private static byte[] ConvertToPcm16Stereo48k(byte[] buffer, int count, int sampleRate, int channels, WaveFormatEncoding encoding)
    {
        // Convert to float samples first
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
        else // 16-bit PCM
        {
            for (int i = 0; i < totalInputFrames; i++)
            {
                int offset = i * channels * 2;
                left[i] = BitConverter.ToInt16(buffer, offset) / 32768.0f;
                right[i] = channels > 1 ? BitConverter.ToInt16(buffer, offset + 2) / 32768.0f : left[i];
            }
        }

        // Resample to 48000 Hz if needed
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
        try { _capture?.StopRecording(); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _capture?.Dispose();
        _device?.Dispose();
    }
}
