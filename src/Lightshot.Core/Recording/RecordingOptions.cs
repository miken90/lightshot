// Ported from LightshotKit/Sources/LightshotKit/RecordingOptions.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Lightshot.Core;

public enum RecordingOutputKind
{
    Video,
    Gif
}

public enum AfterRecordingAction
{
    ShowOverlay,
    SaveSilently,
    OpenEditor
}

public enum VideoCodec
{
    H264,
    Hevc
}

public enum MaxResolution
{
    Original,
    P1080,
    P720
}

public static class MaxResolutionExtensions
{
    public static int? MaxLongestEdge(this MaxResolution res) => res switch
    {
        MaxResolution.Original => null,
        MaxResolution.P1080 => 1920,
        MaxResolution.P720 => 1280,
        _ => null
    };
}

public record VideoSettings
{
    public VideoCodec Codec { get; set; } = VideoCodec.H264;
    public int Fps { get; set; } = 30;
    public MaxResolution MaxResolution { get; set; } = MaxResolution.Original;
    public bool ScaleRetinaTo1x { get; set; } = false;

    public VideoSettings() { }

    public VideoSettings(VideoCodec codec, int fps, MaxResolution maxResolution, bool scaleRetinaTo1x)
    {
        Codec = codec;
        Fps = fps;
        MaxResolution = maxResolution;
        ScaleRetinaTo1x = scaleRetinaTo1x;
    }

    public static readonly VideoSettings Standard = new(VideoCodec.H264, 30, MaxResolution.Original, false);
    public static readonly IReadOnlyList<int> FpsChoices = [10, 15, 30, 60];
}

public record GIFSettings
{
    public int Fps { get; set; } = 15;
    private double _quality = 0.8;
    public double Quality
    {
        get => _quality;
        set => _quality = Math.Clamp(value, 0.0, 1.0);
    }
    public int? MaxWidth { get; set; } = 800;
    public bool Optimize { get; set; } = true;

    public GIFSettings() { }

    public GIFSettings(int fps, double quality, int? maxWidth, bool optimize)
    {
        Fps = fps;
        Quality = Math.Clamp(quality, 0.0, 1.0);
        MaxWidth = maxWidth;
        Optimize = optimize;
    }

    public static readonly GIFSettings Standard = new(15, 0.8, 800, true);
}

public abstract record RecordingOutput
{
    public sealed record Video(VideoSettings Settings) : RecordingOutput;
    public sealed record Gif(GIFSettings Settings) : RecordingOutput;

    public RecordingOutputKind Kind => this is Video ? RecordingOutputKind.Video : RecordingOutputKind.Gif;
}

public abstract record InputDeviceSelection
{
    public sealed record Off : InputDeviceSelection
    {
        public static readonly Off Instance = new();
    }

    public sealed record Device(string? Id) : InputDeviceSelection;

    public bool IsOn => this is Device;
}

public enum CameraBubbleSize
{
    Tiny,
    Small,
    Medium,
    Large,
    Huge
}

public static class CameraBubbleSizeExtensions
{
    public static double Fraction(this CameraBubbleSize size) => size switch
    {
        CameraBubbleSize.Tiny => 0.12,
        CameraBubbleSize.Small => 0.18,
        CameraBubbleSize.Medium => 0.25,
        CameraBubbleSize.Large => 0.33,
        CameraBubbleSize.Huge => 0.45,
        _ => 0.25
    };
}

public enum CameraBubbleShape
{
    Circle,
    Rounded,
    Square
}

public record CameraBubbleSettings
{
    public CameraBubbleSize Size { get; set; } = CameraBubbleSize.Medium;
    public CameraBubbleShape Shape { get; set; } = CameraBubbleShape.Circle;
    public bool Mirror { get; set; } = true;
    public Point? Anchor { get; set; }

    public CameraBubbleSettings() { }

    public CameraBubbleSettings(CameraBubbleSize size = CameraBubbleSize.Medium, CameraBubbleShape shape = CameraBubbleShape.Circle, bool mirror = true, Point? anchor = null)
    {
        Size = size;
        Shape = shape;
        Mirror = mirror;
        Anchor = anchor;
    }

    public static readonly CameraBubbleSettings Standard = new();
}

public static class CameraBubbleLayout
{
    public const double Margin = 16.0;
    public const double MinimumSide = 80.0;

    public static double Side(CameraBubbleSize size, Size region)
    {
        double shorter = Math.Min(region.Width, region.Height);
        double wanted = Math.Max(shorter * size.Fraction(), MinimumSide);
        return Math.Min(wanted, shorter);
    }

    public static Rect Frame(CameraBubbleSettings settings, Size region, bool fullscreen = false)
    {
        if (fullscreen) return new Rect(0, 0, region.Width, region.Height);
        double side = Side(settings.Size, region);
        Point center = settings.Anchor.HasValue
            ? new Point(settings.Anchor.Value.X * region.Width, settings.Anchor.Value.Y * region.Height)
            : new Point(region.Width - Margin - side / 2.0, region.Height - Margin - side / 2.0);
        double x = Math.Min(Math.Max(center.X - side / 2.0, 0.0), Math.Max(region.Width - side, 0.0));
        double y = Math.Min(Math.Max(center.Y - side / 2.0, 0.0), Math.Max(region.Height - side, 0.0));
        return new Rect(x, y, side, side);
    }

    public static Point Anchor(Point center, Size region) =>
        new(
            region.Width > 0 ? Math.Clamp(center.X / region.Width, 0.0, 1.0) : 0.5,
            region.Height > 0 ? Math.Clamp(center.Y / region.Height, 0.0, 1.0) : 0.5
        );
}

public readonly record struct CameraDevice(string Id, string Name, bool IsDefault);

public enum RecordingControlsPosition
{
    Top,
    Bottom
}

public record RecordingDefaults
{
    public VideoSettings Video { get; set; } = VideoSettings.Standard;
    public GIFSettings Gif { get; set; } = GIFSettings.Standard;
    public bool RecordMicrophone { get; set; } = false;
    public string? MicrophoneDeviceID { get; set; }
    private double _microphoneVolume = 1.0;
    public double MicrophoneVolume
    {
        get => _microphoneVolume;
        set => _microphoneVolume = Math.Clamp(value, 0.0, 2.0);
    }
    public bool MonoAudio { get; set; } = false;
    public bool RecordComputerAudio { get; set; } = false;
    private double _computerAudioVolume = 1.0;
    public double ComputerAudioVolume
    {
        get => _computerAudioVolume;
        set => _computerAudioVolume = Math.Clamp(value, 0.0, 2.0);
    }
    public bool SeparateAudioTracks { get; set; } = false;
    public bool RecordCamera { get; set; } = false;
    public string? CameraDeviceID { get; set; }
    public CameraBubbleSettings CameraBubble { get; set; } = CameraBubbleSettings.Standard;
    public bool HighlightClicks { get; set; } = false;
    public ClickHighlightSettings ClickHighlight { get; set; } = ClickHighlightSettings.Standard;
    public bool ShowKeystrokes { get; set; } = false;
    public KeystrokeOverlaySettings KeystrokeOverlay { get; set; } = KeystrokeOverlaySettings.Standard;
    public bool ShowCursor { get; set; } = true;
    public bool CountdownEnabled { get; set; } = true;
    private int _countdownSeconds = 3;
    public int CountdownSeconds
    {
        get => _countdownSeconds;
        set => _countdownSeconds = Math.Max(0, value);
    }
    public bool PlaySounds { get; set; } = true;
    public bool ShowRecordingControls { get; set; } = true;
    public RecordingControlsPosition ControlsPosition { get; set; } = RecordingControlsPosition.Bottom;
    public bool DimScreenWhileRecording { get; set; } = false;
    public bool ConfirmBeforeDiscard { get; set; } = true;
    public bool ShowRecordingTimeInMenuBar { get; set; } = true;
    public AfterRecordingAction AfterRecording { get; set; } = AfterRecordingAction.ShowOverlay;
    public bool HideNotifications { get; set; } = false;

    public RecordingDefaults() { }

    public static readonly RecordingDefaults Standard = new();

    public bool this[RecordingToggle toggle]
    {
        get => toggle switch
        {
            RecordingToggle.Microphone => RecordMicrophone,
            RecordingToggle.ComputerAudio => RecordComputerAudio,
            RecordingToggle.Camera => RecordCamera,
            RecordingToggle.HighlightClicks => HighlightClicks,
            RecordingToggle.ShowKeystrokes => ShowKeystrokes,
            _ => false
        };
        set
        {
            switch (toggle)
            {
                case RecordingToggle.Microphone: RecordMicrophone = value; break;
                case RecordingToggle.ComputerAudio: RecordComputerAudio = value; break;
                case RecordingToggle.Camera: RecordCamera = value; break;
                case RecordingToggle.HighlightClicks: HighlightClicks = value; break;
                case RecordingToggle.ShowKeystrokes: ShowKeystrokes = value; break;
            }
        }
    }
}

public enum RecordingToggle
{
    Microphone,
    ComputerAudio,
    Camera,
    HighlightClicks,
    ShowKeystrokes
}

public static class RecordingToggleExtensions
{
    public static PermissionKind? RequiredPermission(this RecordingToggle toggle) => toggle switch
    {
        RecordingToggle.Microphone => PermissionKind.Microphone,
        RecordingToggle.Camera => PermissionKind.Camera,
        RecordingToggle.ShowKeystrokes => PermissionKind.InputMonitoring,
        _ => null
    };
}

public record RecordingOverrides
{
    public bool? Microphone { get; set; }
    public bool? ComputerAudio { get; set; }
    public bool? Camera { get; set; }
    public bool? HighlightClicks { get; set; }
    public bool? ShowKeystrokes { get; set; }
    public AfterRecordingAction? AfterRecording { get; set; }
    public bool Studio { get; set; } = false;

    public RecordingOverrides() { }

    public RecordingOverrides(
        bool? microphone = null,
        bool? computerAudio = null,
        bool? camera = null,
        bool? highlightClicks = null,
        bool? showKeystrokes = null,
        AfterRecordingAction? afterRecording = null,
        bool studio = false)
    {
        Microphone = microphone;
        ComputerAudio = computerAudio;
        Camera = camera;
        HighlightClicks = highlightClicks;
        ShowKeystrokes = showKeystrokes;
        AfterRecording = afterRecording;
        Studio = studio;
    }

    public static readonly RecordingOverrides None = new();

    public bool? this[RecordingToggle toggle]
    {
        get => toggle switch
        {
            RecordingToggle.Microphone => Microphone,
            RecordingToggle.ComputerAudio => ComputerAudio,
            RecordingToggle.Camera => Camera,
            RecordingToggle.HighlightClicks => HighlightClicks,
            RecordingToggle.ShowKeystrokes => ShowKeystrokes,
            _ => null
        };
        set
        {
            switch (toggle)
            {
                case RecordingToggle.Microphone: Microphone = value; break;
                case RecordingToggle.ComputerAudio: ComputerAudio = value; break;
                case RecordingToggle.Camera: Camera = value; break;
                case RecordingToggle.HighlightClicks: HighlightClicks = value; break;
                case RecordingToggle.ShowKeystrokes: ShowKeystrokes = value; break;
            }
        }
    }
}

public record RecordingChoice(
    CaptureRegion Region,
    RecordingOutputKind Output,
    RecordingOverrides Overrides,
    string? MicrophoneDeviceID = null,
    string? CameraDeviceID = null)
{
    public RecordingChoice(CaptureRegion region, RecordingOutputKind output)
        : this(region, output, RecordingOverrides.None) { }
}

public record RecordingOptions
{
    public CaptureRegion Region { get; init; }
    public RecordingOutput Output { get; init; }
    public InputDeviceSelection Microphone { get; init; }
    public bool ComputerAudio { get; init; }
    public double MicrophoneVolume { get; init; }
    public bool MonoAudio { get; init; }
    public double ComputerAudioVolume { get; init; }
    public bool SeparateAudioTracks { get; init; }
    public InputDeviceSelection Camera { get; init; }
    public CameraBubbleSettings CameraBubble { get; init; }
    public bool HighlightClicks { get; init; }
    public ClickHighlightSettings ClickHighlight { get; init; }
    public bool ShowKeystrokes { get; init; }
    public KeystrokeOverlaySettings KeystrokeOverlay { get; init; }
    public bool ShowCursor { get; init; }
    public int CountdownSeconds { get; init; }
    public AfterRecordingAction AfterRecording { get; init; }
    public bool Studio { get; init; }
    public bool HideDesktopIcons { get; init; }
    public bool HideNotifications { get; init; }

    public RecordingOptions(
        CaptureRegion region,
        RecordingOutput output,
        InputDeviceSelection? microphone = null,
        bool computerAudio = false,
        double microphoneVolume = 1.0,
        bool monoAudio = false,
        double computerAudioVolume = 1.0,
        bool separateAudioTracks = false,
        InputDeviceSelection? camera = null,
        CameraBubbleSettings? cameraBubble = null,
        bool highlightClicks = false,
        ClickHighlightSettings? clickHighlight = null,
        bool showKeystrokes = false,
        KeystrokeOverlaySettings? keystrokeOverlay = null,
        bool showCursor = true,
        int countdownSeconds = 0,
        AfterRecordingAction afterRecording = AfterRecordingAction.ShowOverlay,
        bool studio = false,
        bool hideDesktopIcons = false,
        bool hideNotifications = false)
    {
        Region = region;
        Output = output;
        Microphone = microphone ?? InputDeviceSelection.Off.Instance;
        ComputerAudio = computerAudio;
        MicrophoneVolume = Math.Clamp(microphoneVolume, 0.0, 2.0);
        MonoAudio = monoAudio;
        ComputerAudioVolume = Math.Clamp(computerAudioVolume, 0.0, 2.0);
        SeparateAudioTracks = separateAudioTracks;
        Camera = camera ?? InputDeviceSelection.Off.Instance;
        CameraBubble = cameraBubble ?? CameraBubbleSettings.Standard;
        HighlightClicks = highlightClicks;
        ClickHighlight = clickHighlight ?? ClickHighlightSettings.Standard;
        ShowKeystrokes = showKeystrokes;
        KeystrokeOverlay = keystrokeOverlay ?? KeystrokeOverlaySettings.Standard;
        ShowCursor = showCursor;
        CountdownSeconds = Math.Max(0, countdownSeconds);
        AfterRecording = afterRecording;
        Studio = studio;
        HideDesktopIcons = hideDesktopIcons;
        HideNotifications = hideNotifications;
    }

    public bool HasCountdown => CountdownSeconds > 0;

    public static RecordingOptions Resolve(
        CaptureRegion region,
        RecordingOutputKind output,
        RecordingDefaults defaults,
        RecordingOverrides? overrides = null,
        bool hideDesktopIcons = false)
    {
        var ov = overrides ?? RecordingOverrides.None;
        bool microphoneOn = ov.Microphone ?? defaults.RecordMicrophone;
        bool cameraOn = ov.Camera ?? defaults.RecordCamera;

        RecordingOutput recordingOutput = output == RecordingOutputKind.Video
            ? new RecordingOutput.Video(defaults.Video)
            : new RecordingOutput.Gif(defaults.Gif);

        return new RecordingOptions(
            region: region,
            output: recordingOutput,
            microphone: microphoneOn ? new InputDeviceSelection.Device(defaults.MicrophoneDeviceID) : InputDeviceSelection.Off.Instance,
            computerAudio: ov.ComputerAudio ?? defaults.RecordComputerAudio,
            microphoneVolume: defaults.MicrophoneVolume,
            monoAudio: defaults.MonoAudio,
            computerAudioVolume: defaults.ComputerAudioVolume,
            separateAudioTracks: defaults.SeparateAudioTracks,
            camera: cameraOn ? new InputDeviceSelection.Device(defaults.CameraDeviceID) : InputDeviceSelection.Off.Instance,
            cameraBubble: defaults.CameraBubble,
            highlightClicks: ov.HighlightClicks ?? defaults.HighlightClicks,
            clickHighlight: defaults.ClickHighlight,
            showKeystrokes: ov.ShowKeystrokes ?? defaults.ShowKeystrokes,
            keystrokeOverlay: defaults.KeystrokeOverlay,
            showCursor: defaults.ShowCursor,
            countdownSeconds: defaults.CountdownEnabled ? defaults.CountdownSeconds : 0,
            afterRecording: ov.AfterRecording ?? defaults.AfterRecording,
            studio: ov.Studio,
            hideDesktopIcons: hideDesktopIcons,
            hideNotifications: defaults.HideNotifications
        );
    }
}
