// Ported for Lightshot Windows Port (Phase 7 R3)
// MIT License, Copyright (c) 2026 Viet Le

namespace Lightshot.Platform.Windows.Audio;

/// <summary>
/// Audio track configuration for Media Foundation AAC encoding.
/// Standard AAC encoding: 48 kHz, 96 kbps mono or 160 kbps stereo.
/// </summary>
public readonly record struct AudioTrackConfig(int Channels, int Bitrate, int SampleRate = 48000)
{
    public static AudioTrackConfig Mono => new(1, 96_000, 48000);
    public static AudioTrackConfig Stereo => new(2, 160_000, 48000);
}
