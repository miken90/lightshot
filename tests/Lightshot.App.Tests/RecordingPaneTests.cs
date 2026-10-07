// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Lightshot.App.Views.Settings;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.App.Tests;

public class RecordingPaneTests
{
    private sealed class MemorySettingsStore : ISettingsStore
    {
        public ImageFormat DefaultFormat { get; set; } = new ImageFormat.Png();
        public string SaveLocation { get; set; } = @"C:\Users\Test\Pictures";
        public string FilenamePattern { get; set; } = "Screenshot_%Y%m%d";
        public HotkeyBindings Hotkeys { get; set; } = HotkeyBindings.Defaults;
        public bool OpenInEditor { get; set; } = true;
        public bool IncludeCursor { get; set; } = false;
        public double CaptureDelay { get; set; } = 0;
        public int HistoryRetention { get; set; } = 50;
        public int HistoryMaxAgeDays { get; set; } = 7;
        public bool LaunchAtLogin { get; set; } = false;
        public RecordingDefaults RecordingDefaults { get; set; } = new();
        public bool RememberLastRecordingArea { get; set; } = false;
        public CaptureRegion? LastRecordingRegion { get; set; }
        public AppearancePreference Appearance { get; set; } = AppearancePreference.System;
        public bool OcrKeepsLineBreaks { get; set; } = true;
        public bool HideDesktopIcons { get; set; } = false;
        public bool AdjustAreaBeforeCapture { get; set; } = false;
        public QuickAccessSettings QuickAccess { get; set; } = new();
        public AfterCaptureSettings AfterCapture { get; set; } = new();
    }

    [Fact]
    [Unit]
    public void EveryRecordingDefaultsFieldIsBoundInRecordingPane()
    {
        // 1. Spec Step 11 list of required fields:
        // codec, fps, max resolution, scale, GIF fps/width/quality/optimise,
        // audio options and volumes, mono, separate tracks, cursor,
        // click highlight style/colour/size/animate,
        // keystroke mode/position/size/appearance/blur,
        // controls position, show controls, show time, dim screen, confirm discard,
        // hide notifications, hide desktop icons, countdown, sounds.
        var expectedSpecFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Video
            nameof(RecordingDefaults.Video),
            // GIF
            nameof(RecordingDefaults.Gif),
            // Audio
            nameof(RecordingDefaults.RecordMicrophone),
            nameof(RecordingDefaults.MicrophoneVolume),
            nameof(RecordingDefaults.MonoAudio),
            nameof(RecordingDefaults.RecordComputerAudio),
            nameof(RecordingDefaults.ComputerAudioVolume),
            nameof(RecordingDefaults.SeparateAudioTracks),
            // Camera
            nameof(RecordingDefaults.RecordCamera),
            nameof(RecordingDefaults.CameraBubble),
            // Cursor & Clicks
            nameof(RecordingDefaults.ShowCursor),
            nameof(RecordingDefaults.HighlightClicks),
            nameof(RecordingDefaults.ClickHighlight),
            // Keystrokes
            nameof(RecordingDefaults.ShowKeystrokes),
            nameof(RecordingDefaults.KeystrokeOverlay),
            // While recording behavior
            nameof(RecordingDefaults.CountdownEnabled),
            nameof(RecordingDefaults.CountdownSeconds),
            nameof(RecordingDefaults.PlaySounds),
            nameof(RecordingDefaults.ShowRecordingControls),
            nameof(RecordingDefaults.ControlsPosition),
            nameof(RecordingDefaults.DimScreenWhileRecording),
            nameof(RecordingDefaults.ConfirmBeforeDiscard),
            nameof(RecordingDefaults.ShowRecordingTimeInMenuBar),
            nameof(RecordingDefaults.AfterRecording),
            nameof(RecordingDefaults.HideNotifications)
        };

        // Verify that RecordingDefaults contains all expected properties
        var defaultsProps = typeof(RecordingDefaults).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var defaultsPropNames = defaultsProps.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var specField in expectedSpecFields)
        {
            Assert.True(defaultsPropNames.Contains(specField),
                $"RecordingDefaults should declare property '{specField}' from spec.");
        }

        // 2. Read RecordingPane.xaml content to verify bindings exist for all fields
        string xamlPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Lightshot.App", "Views", "Settings", "RecordingPane.xaml");
        if (!File.Exists(xamlPath))
        {
            // Fallback path if running in different relative test runner directory
            xamlPath = Path.GetFullPath(@"..\..\..\..\..\src\Lightshot.App\Views\Settings\RecordingPane.xaml");
        }
        if (!File.Exists(xamlPath))
        {
            // Search in workspace
            xamlPath = Directory.GetFiles(Directory.GetCurrentDirectory(), "RecordingPane.xaml", SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new FileNotFoundException("RecordingPane.xaml not found for reflection test.");
        }

        string xamlContent = File.ReadAllText(xamlPath);
        var matches = Regex.Matches(xamlContent, @"\{Binding\s+([A-Za-z0-9_]+)");
        var boundPropertyNames = matches.Select(m => m.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Required bindings in RecordingPane.xaml
        var expectedPaneBindings = new[]
        {
            "VideoFps",
            "VideoMaxResolution",
            "VideoCodec",
            "VideoScaleRetinaTo1x",
            "GifFps",
            "GifQuality",
            "GifMaxWidth",
            "GifOptimize",
            "RecordMicrophone",
            "MicrophoneVolume",
            "RecordComputerAudio",
            "ComputerAudioVolume",
            "MonoAudio",
            "SeparateAudioTracks",
            "RecordCamera",
            "CameraBubbleSize",
            "CameraBubbleShape",
            "CameraBubbleMirror",
            "ShowCursor",
            "HighlightClicks",
            "ClickHighlightStyle",
            "ClickHighlightSize",
            "ClickHighlightColor",
            "ClickHighlightAnimateClicks",
            "ClickHighlight",
            "ShowKeystrokes",
            "KeystrokeMode",
            "KeystrokePosition",
            "KeystrokeSize",
            "KeystrokeAppearance",
            "KeystrokeBlurBackground",
            "CountdownEnabled",
            "CountdownSeconds",
            "PlaySounds",
            "ShowRecordingControls",
            "ControlsPosition",
            "DimScreenWhileRecording",
            "HideNotifications",
            "ConfirmBeforeDiscard",
            "ShowRecordingTimeInMenuBar",
            "AfterRecording"
        };

        foreach (var binding in expectedPaneBindings)
        {
            Assert.True(boundPropertyNames.Contains(binding),
                $"RecordingPane.xaml should bind to SettingsViewModel property '{binding}'");
        }
    }

    [Fact]
    [Unit]
    public void SettingsViewModelRoundtripsRecordingDefaultsFields()
    {
        var store = new MemorySettingsStore();
        var vm = new SettingsViewModel(store);

        // Test video fields
        vm.VideoFps = 60;
        Assert.Equal(60, store.RecordingDefaults.Video.Fps);

        vm.VideoCodec = VideoCodec.Hevc;
        Assert.Equal(VideoCodec.Hevc, store.RecordingDefaults.Video.Codec);

        vm.VideoMaxResolution = MaxResolution.P1080;
        Assert.Equal(MaxResolution.P1080, store.RecordingDefaults.Video.MaxResolution);

        vm.VideoScaleRetinaTo1x = true;
        Assert.True(store.RecordingDefaults.Video.ScaleRetinaTo1x);

        // Test GIF fields
        vm.GifFps = 30;
        Assert.Equal(30, store.RecordingDefaults.Gif.Fps);

        vm.GifQuality = 0.95;
        Assert.Equal(0.95, store.RecordingDefaults.Gif.Quality);

        vm.GifMaxWidth = 640;
        Assert.Equal(640, store.RecordingDefaults.Gif.MaxWidth);

        vm.GifOptimize = false;
        Assert.False(store.RecordingDefaults.Gif.Optimize);

        // Test audio fields
        vm.RecordMicrophone = true;
        Assert.True(store.RecordingDefaults.RecordMicrophone);

        vm.MicrophoneVolume = 1.5;
        Assert.Equal(1.5, store.RecordingDefaults.MicrophoneVolume);

        vm.RecordComputerAudio = true;
        Assert.True(store.RecordingDefaults.RecordComputerAudio);

        vm.ComputerAudioVolume = 0.8;
        Assert.Equal(0.8, store.RecordingDefaults.ComputerAudioVolume);

        vm.MonoAudio = true;
        Assert.True(store.RecordingDefaults.MonoAudio);

        vm.SeparateAudioTracks = true;
        Assert.True(store.RecordingDefaults.SeparateAudioTracks);

        // Test camera fields
        vm.RecordCamera = true;
        Assert.True(store.RecordingDefaults.RecordCamera);

        vm.CameraBubbleSize = CameraBubbleSize.Large;
        Assert.Equal(CameraBubbleSize.Large, store.RecordingDefaults.CameraBubble.Size);

        vm.CameraBubbleShape = CameraBubbleShape.Square;
        Assert.Equal(CameraBubbleShape.Square, store.RecordingDefaults.CameraBubble.Shape);

        vm.CameraBubbleMirror = false;
        Assert.False(store.RecordingDefaults.CameraBubble.Mirror);

        // Test click highlights
        vm.HighlightClicks = true;
        Assert.True(store.RecordingDefaults.HighlightClicks);

        vm.ClickHighlightStyle = CursorHighlightStyle.Filled;
        Assert.Equal(CursorHighlightStyle.Filled, store.RecordingDefaults.ClickHighlight.Style);

        vm.ClickHighlightSize = CursorHighlightSize.Large;
        Assert.Equal(CursorHighlightSize.Large, store.RecordingDefaults.ClickHighlight.Size);

        vm.ClickHighlightColor = CursorHighlightColor.Red;
        Assert.Equal(CursorHighlightColor.Red, store.RecordingDefaults.ClickHighlight.Color);

        vm.ClickHighlightAnimateClicks = false;
        Assert.False(store.RecordingDefaults.ClickHighlight.AnimateClicks);

        // Test keystrokes
        vm.ShowKeystrokes = true;
        Assert.True(store.RecordingDefaults.ShowKeystrokes);

        vm.KeystrokeMode = KeystrokeDisplayMode.CommandOnly;
        Assert.Equal(KeystrokeDisplayMode.CommandOnly, store.RecordingDefaults.KeystrokeOverlay.Mode);

        vm.KeystrokePosition = KeystrokeOverlayPosition.TopLeft;
        Assert.Equal(KeystrokeOverlayPosition.TopLeft, store.RecordingDefaults.KeystrokeOverlay.Position);

        vm.KeystrokeSize = KeystrokeOverlaySize.Large;
        Assert.Equal(KeystrokeOverlaySize.Large, store.RecordingDefaults.KeystrokeOverlay.Size);

        vm.KeystrokeAppearance = KeystrokeOverlayAppearance.Dark;
        Assert.Equal(KeystrokeOverlayAppearance.Dark, store.RecordingDefaults.KeystrokeOverlay.Appearance);

        vm.KeystrokeBlurBackground = false;
        Assert.False(store.RecordingDefaults.KeystrokeOverlay.BlurBackground);

        // Test behavior
        vm.CountdownEnabled = false;
        Assert.False(store.RecordingDefaults.CountdownEnabled);

        vm.CountdownSeconds = 5;
        Assert.Equal(5, store.RecordingDefaults.CountdownSeconds);

        vm.PlaySounds = false;
        Assert.False(store.RecordingDefaults.PlaySounds);

        vm.ShowRecordingControls = false;
        Assert.False(store.RecordingDefaults.ShowRecordingControls);

        vm.ControlsPosition = RecordingControlsPosition.Top;
        Assert.Equal(RecordingControlsPosition.Top, store.RecordingDefaults.ControlsPosition);

        vm.DimScreenWhileRecording = true;
        Assert.True(store.RecordingDefaults.DimScreenWhileRecording);

        vm.ConfirmBeforeDiscard = false;
        Assert.False(store.RecordingDefaults.ConfirmBeforeDiscard);

        vm.ShowRecordingTimeInMenuBar = false;
        Assert.False(store.RecordingDefaults.ShowRecordingTimeInMenuBar);

        vm.AfterRecording = AfterRecordingAction.OpenEditor;
        Assert.Equal(AfterRecordingAction.OpenEditor, store.RecordingDefaults.AfterRecording);

        vm.HideNotifications = true;
        Assert.True(store.RecordingDefaults.HideNotifications);
    }
}
