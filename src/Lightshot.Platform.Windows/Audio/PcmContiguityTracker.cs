// Lightshot Windows Port
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Buffers.Binary;
using NAudio.Wave;

namespace Lightshot.Platform.Windows.Audio;

/// <summary>
/// Aligns incoming WASAPI audio packets against QPC device timestamps and maintains strict stream contiguity.
/// Inserts silence padding for gaps and trims overlapping frames.
/// </summary>
public sealed class PcmContiguityTracker
{
    private readonly int _channels;
    private readonly double _sampleRate;
    private long _nextExpectedFrameIndex;
    private bool _hasReceivedFirstPacket;

    public int Channels => _channels;
    public double SampleRate => _sampleRate;
    public long NextExpectedFrameIndex => _nextExpectedFrameIndex;

    public PcmContiguityTracker(int channels = 2, double sampleRate = 48000.0)
    {
        _channels = Math.Max(1, channels);
        _sampleRate = sampleRate > 0 ? sampleRate : 48000.0;
        Reset();
    }

    public void Reset()
    {
        _nextExpectedFrameIndex = 0;
        _hasReceivedFirstPacket = false;
    }

    /// <summary>
    /// Computes presentation timestamp in HNS from QPC position, pause duration, and start instant.
    /// </summary>
    public static long ComputeTimestampHns(long qpcPositionHns, long pauseOffsetHns, long startQpcHns)
    {
        long baseQpc = qpcPositionHns > 0 ? qpcPositionHns : startQpcHns;
        long elapsedHns = baseQpc - pauseOffsetHns - startQpcHns;
        return Math.Max(0, elapsedHns);
    }

    /// <summary>
    /// Processes incoming float samples, inserting silence or trimming overlaps to ensure contiguity.
    /// </summary>
    public PcmFrames Align(float[] incomingSamples, long targetTimestampHns)
    {
        if (incomingSamples == null || incomingSamples.Length == 0)
        {
            return PcmFrames.CreateSilence(0, _channels, _sampleRate, targetTimestampHns);
        }

        int incomingFrameCount = incomingSamples.Length / _channels;
        long targetFrameIndex = (long)Math.Round((targetTimestampHns / 10_000_000.0) * _sampleRate);

        if (!_hasReceivedFirstPacket)
        {
            _hasReceivedFirstPacket = true;
            if (targetFrameIndex > 0)
            {
                // Pad initial gap from start of recording with silence
                int silenceFrames = (int)Math.Min(targetFrameIndex, 48000 * 5); // cap at 5 seconds sanity limit
                float[] combined = new float[(silenceFrames + incomingFrameCount) * _channels];
                Array.Copy(incomingSamples, 0, combined, silenceFrames * _channels, incomingSamples.Length);

                long startHns = 0;
                var frames = new PcmFrames(combined, _channels, _sampleRate, startHns);
                _nextExpectedFrameIndex = silenceFrames + incomingFrameCount;
                return frames;
            }

            if (targetFrameIndex < 0)
            {
                // Packet started before recording start instant; trim leading frames
                int trimFrames = (int)Math.Min(-targetFrameIndex, incomingFrameCount);
                int remainingFrames = incomingFrameCount - trimFrames;
                float[] trimmed = new float[remainingFrames * _channels];
                Array.Copy(incomingSamples, trimFrames * _channels, trimmed, 0, trimmed.Length);

                var frames = new PcmFrames(trimmed, _channels, _sampleRate, 0);
                _nextExpectedFrameIndex = remainingFrames;
                return frames;
            }

            _nextExpectedFrameIndex = incomingFrameCount;
            return new PcmFrames(incomingSamples, _channels, _sampleRate, targetTimestampHns);
        }

        long gapFrames = targetFrameIndex - _nextExpectedFrameIndex;

        // Tolerant contiguity: within +/- 2 ms (e.g. 96 frames at 48k), consider continuous
        int jitterToleranceFrames = (int)(_sampleRate * 0.002);

        if (gapFrames > jitterToleranceFrames)
        {
            // Gap detected: pad with silence
            int padFrames = (int)Math.Min(gapFrames, 48000 * 5);
            float[] combined = new float[(padFrames + incomingFrameCount) * _channels];
            Array.Copy(incomingSamples, 0, combined, padFrames * _channels, incomingSamples.Length);

            long presentationHns = (long)((_nextExpectedFrameIndex / _sampleRate) * 10_000_000.0);
            var frames = new PcmFrames(combined, _channels, _sampleRate, presentationHns);
            _nextExpectedFrameIndex += padFrames + incomingFrameCount;
            return frames;
        }

        if (gapFrames < -jitterToleranceFrames)
        {
            // Overlap detected: trim overlapping prefix from incoming samples
            int overlapFrames = (int)Math.Min(-gapFrames, incomingFrameCount);
            int remainingFrames = incomingFrameCount - overlapFrames;
            float[] trimmed = new float[remainingFrames * _channels];
            Array.Copy(incomingSamples, overlapFrames * _channels, trimmed, 0, trimmed.Length);

            long presentationHns = (long)((_nextExpectedFrameIndex / _sampleRate) * 10_000_000.0);
            var frames = new PcmFrames(trimmed, _channels, _sampleRate, presentationHns);
            _nextExpectedFrameIndex += remainingFrames;
            return frames;
        }

        // Continuous packet within jitter tolerance
        long continuousHns = (long)((_nextExpectedFrameIndex / _sampleRate) * 10_000_000.0);
        _nextExpectedFrameIndex += incomingFrameCount;
        return new PcmFrames(incomingSamples, _channels, _sampleRate, continuousHns);
    }

    /// <summary>
    /// Converts raw byte buffers of any WaveFormat into 32-bit float samples at target channels and sample rate.
    /// </summary>
    public static float[] ConvertToFloatSamples(ReadOnlySpan<byte> buffer, WaveFormat format, int targetChannels = 2, double targetRate = 48000.0)
    {
        return PcmSampleConverter.ConvertToFloatSamples(buffer, format, targetChannels, targetRate);
    }
}
