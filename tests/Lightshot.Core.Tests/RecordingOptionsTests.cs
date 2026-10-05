// Ported from LightshotKit/Tests/LightshotKitTests/RecordingOptionsTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Core.Tests;

public class RecordingOptionsTests
{
    private static readonly CaptureRegion TestRegion = new CaptureRegion.RectRegion(new Rect(0, 0, 100, 50));

    private static RecordingDefaults CreateAllOnDefaults() => new()
    {
        Video = new VideoSettings(VideoCodec.Hevc, 60, MaxResolution.P1080, true),
        Gif = new GIFSettings(10, 0.5, 480, false),
        RecordMicrophone = true,
        MicrophoneDeviceID = "mic-1",
        RecordComputerAudio = true,
        RecordCamera = true,
        CameraDeviceID = "cam-1",
        HighlightClicks = true,
        ShowKeystrokes = true,
        ShowCursor = true,
        CountdownEnabled = true,
        CountdownSeconds = 5
    };

    [Fact]
    [Unit]
    public void WithNoOverridesEveryValueComesFromDefaults()
    {
        var allOn = CreateAllOnDefaults();
        var o = RecordingOptions.Resolve(TestRegion, RecordingOutputKind.Video, allOn);
        Assert.Equal(TestRegion, o.Region);
        Assert.Equal(new RecordingOutput.Video(allOn.Video), o.Output);
        Assert.Equal(new InputDeviceSelection.Device("mic-1"), o.Microphone);
        Assert.True(o.ComputerAudio);
        Assert.Equal(new InputDeviceSelection.Device("cam-1"), o.Camera);
        Assert.True(o.HighlightClicks);
        Assert.True(o.ShowKeystrokes);
        Assert.True(o.ShowCursor);
        Assert.Equal(5, o.CountdownSeconds);
        Assert.True(o.HasCountdown);
    }

    [Fact]
    [Unit]
    public void StartGIFPicksTheGIFSettings()
    {
        var allOn = CreateAllOnDefaults();
        var o = RecordingOptions.Resolve(TestRegion, RecordingOutputKind.Gif, allOn);
        Assert.Equal(new RecordingOutput.Gif(allOn.Gif), o.Output);
        Assert.Equal(RecordingOutputKind.Gif, o.Output.Kind);
    }

    [Fact]
    [Unit]
    public void EveryOverrideBeatsItsDefaultInBothDirections()
    {
        var allOn = CreateAllOnDefaults();
        var allOff = new RecordingOverrides(
            microphone: false, computerAudio: false, camera: false, highlightClicks: false, showKeystrokes: false
        );
        var off = RecordingOptions.Resolve(TestRegion, RecordingOutputKind.Video, allOn, overrides: allOff);
        Assert.Equal(InputDeviceSelection.Off.Instance, off.Microphone);
        Assert.False(off.ComputerAudio);
        Assert.Equal(InputDeviceSelection.Off.Instance, off.Camera);
        Assert.False(off.HighlightClicks);
        Assert.False(off.ShowKeystrokes);
        Assert.True(off.ShowCursor);
        Assert.Equal(5, off.CountdownSeconds);

        var allOnOv = new RecordingOverrides(
            microphone: true, computerAudio: true, camera: true, highlightClicks: true, showKeystrokes: true
        );
        var on = RecordingOptions.Resolve(TestRegion, RecordingOutputKind.Video, RecordingDefaults.Standard, overrides: allOnOv);
        Assert.Equal(new InputDeviceSelection.Device(null), on.Microphone);
        Assert.True(on.ComputerAudio);
        Assert.Equal(new InputDeviceSelection.Device(null), on.Camera);
        Assert.True(on.HighlightClicks);
        Assert.True(on.ShowKeystrokes);
    }

    [Fact]
    [Unit]
    public void TurningASourceOnByOverrideUsesTheDefaultDevice()
    {
        var defaults = new RecordingDefaults { MicrophoneDeviceID = "mic-2", CameraDeviceID = null };
        var o = RecordingOptions.Resolve(
            TestRegion, RecordingOutputKind.Video, defaults,
            overrides: new RecordingOverrides(microphone: true, camera: true)
        );
        Assert.Equal(new InputDeviceSelection.Device("mic-2"), o.Microphone);
        Assert.Equal(new InputDeviceSelection.Device(null), o.Camera);
        Assert.True(o.Camera.IsOn);
        Assert.False(InputDeviceSelection.Off.Instance.IsOn);
    }

    [Fact]
    [Unit]
    public void OverridesAreValuesAndNeverMutateTheDefaults()
    {
        var defaults = RecordingDefaults.Standard;
        _ = RecordingOptions.Resolve(
            TestRegion, RecordingOutputKind.Video, defaults,
            overrides: new RecordingOverrides(microphone: true, computerAudio: true)
        );
        Assert.Equal(RecordingDefaults.Standard, defaults);
        Assert.Equal(RecordingOverrides.None, new RecordingOverrides());
        Assert.True(RecordingDefaults.Standard.CountdownEnabled);
        Assert.True(RecordingDefaults.Standard.PlaySounds);
    }

    [Fact]
    [Unit]
    public void AStoredBlobFromBeforePlaySoundsStillDecodes()
    {
        var node = JsonSerializer.SerializeToNode(new RecordingDefaults { PlaySounds = false })!.AsObject();
        node.Remove("PlaySounds");
        var decoded = node.Deserialize<RecordingDefaults>()!;
        Assert.True(decoded.PlaySounds);

        string[] olderKeys = [
            "ShowRecordingControls", "ControlsPosition", "DimScreenWhileRecording",
            "ConfirmBeforeDiscard", "ShowRecordingTimeInMenuBar", "MicrophoneVolume",
            "MonoAudio", "ComputerAudioVolume", "SeparateAudioTracks", "ClickHighlight",
            "KeystrokeOverlay", "CameraBubble", "AfterRecording"
        ];
        foreach (var key in olderKeys)
        {
            node.Remove(key);
        }
        var older = node.Deserialize<RecordingDefaults>()!;
        Assert.True(older.ShowRecordingControls && older.ControlsPosition == RecordingControlsPosition.Bottom && !older.DimScreenWhileRecording && older.ConfirmBeforeDiscard);
        Assert.True(older.ShowRecordingTimeInMenuBar);
        Assert.True(older.MicrophoneVolume == 1 && !older.MonoAudio);
        Assert.True(older.ComputerAudioVolume == 1 && !older.SeparateAudioTracks);
        Assert.Equal(ClickHighlightSettings.Standard, older.ClickHighlight);
        Assert.Equal(KeystrokeOverlaySettings.Standard, older.KeystrokeOverlay);
        Assert.Equal(CameraBubbleSettings.Standard, older.CameraBubble);
        Assert.Equal(AfterRecordingAction.ShowOverlay, older.AfterRecording);
    }

    [Fact]
    [Unit]
    public void CameraBubbleSettingsReachTheOptions()
    {
        var look = new CameraBubbleSettings(CameraBubbleSize.Huge, CameraBubbleShape.Square, false, new Point(0.1, 0.2));
        var o = RecordingOptions.Resolve(
            TestRegion, RecordingOutputKind.Video,
            new RecordingDefaults { CameraDeviceID = "cam", CameraBubble = look },
            overrides: new RecordingOverrides(camera: true)
        );
        Assert.True(o.Camera == new InputDeviceSelection.Device("cam") && o.CameraBubble == look);
    }

    [Fact]
    [Unit]
    public void ToggleSubscriptsReadDefaultsAndWriteOverrides()
    {
        var defaults = new RecordingDefaults { RecordMicrophone = true, RecordCamera = true };
        Assert.True(defaults[RecordingToggle.Microphone] && defaults[RecordingToggle.Camera] && !defaults[RecordingToggle.ComputerAudio]);

        var overrides = new RecordingOverrides();
        overrides[RecordingToggle.Microphone] = false;
        overrides[RecordingToggle.ShowKeystrokes] = true;
        Assert.Equal(new RecordingOverrides(microphone: false, showKeystrokes: true), overrides);
        Assert.Null(overrides[RecordingToggle.Camera]);

        var choice = new RecordingChoice(new CaptureRegion.DisplayRegion(1), RecordingOutputKind.Gif, overrides);
        Assert.Equal(InputDeviceSelection.Off.Instance, RecordingOptions.Resolve(choice.Region, choice.Output, defaults, overrides: choice.Overrides).Microphone);
    }

    [Fact]
    [Unit]
    public void CountdownOffInDefaultsMeansNoCountdownEvenWithSeconds()
    {
        var defaults = new RecordingDefaults { CountdownEnabled = false, CountdownSeconds = 3 };
        var o = RecordingOptions.Resolve(TestRegion, RecordingOutputKind.Video, defaults);
        Assert.Equal(0, o.CountdownSeconds);
        Assert.False(o.HasCountdown);
    }

    [Fact]
    [Unit]
    public void ClickHighlightSettingsReachTheOptions()
    {
        var look = new ClickHighlightSettings(CursorHighlightStyle.Outline, CursorHighlightSize.Large, CursorHighlightColor.Red, false);
        var o = RecordingOptions.Resolve(
            TestRegion, RecordingOutputKind.Video,
            new RecordingDefaults { ClickHighlight = look },
            overrides: new RecordingOverrides(highlightClicks: true)
        );
        Assert.True(o.HighlightClicks && o.ClickHighlight == look);
    }

    [Fact]
    [Unit]
    public void KeystrokeOverlaySettingsReachTheOptions()
    {
        var look = new KeystrokeOverlaySettings(KeystrokeDisplayMode.CommandOnly, KeystrokeOverlayPosition.TopRight, KeystrokeOverlaySize.Large, KeystrokeOverlayAppearance.Dark, false);
        var o = RecordingOptions.Resolve(
            TestRegion, RecordingOutputKind.Video,
            new RecordingDefaults { KeystrokeOverlay = look },
            overrides: new RecordingOverrides(showKeystrokes: true)
        );
        Assert.True(o.ShowKeystrokes && o.KeystrokeOverlay == look);
        Assert.False(RecordingOptions.Resolve(TestRegion, RecordingOutputKind.Video, new RecordingDefaults { KeystrokeOverlay = look }).ShowKeystrokes);
    }

    [Fact]
    [Unit]
    public void ComputerAudioSettingsReachTheOptions()
    {
        var defaults = new RecordingDefaults { RecordComputerAudio = true, ComputerAudioVolume = 0.5, SeparateAudioTracks = true };
        var o = RecordingOptions.Resolve(TestRegion, RecordingOutputKind.Video, defaults);
        Assert.True(o.ComputerAudio && o.ComputerAudioVolume == 0.5 && o.SeparateAudioTracks);
        Assert.Equal(2.0, new RecordingDefaults { ComputerAudioVolume = 9 }.ComputerAudioVolume);
    }

    [Fact]
    [Unit]
    public void MicrophoneVolumeIsClampedToTwiceUnity()
    {
        Assert.Equal(2.0, new RecordingDefaults { MicrophoneVolume = 5 }.MicrophoneVolume);
        Assert.Equal(0.0, new RecordingDefaults { MicrophoneVolume = -1 }.MicrophoneVolume);
        Assert.Equal(2.0, new RecordingOptions(TestRegion, new RecordingOutput.Video(VideoSettings.Standard), microphoneVolume: 3).MicrophoneVolume);
    }

    [Fact]
    [Unit]
    public void GifQualityIsClampedAndNegativeCountdownsAreZero()
    {
        Assert.Equal(1.0, new GIFSettings(15, 1.7, null, true).Quality);
        Assert.Equal(0.0, new GIFSettings(15, -1, null, true).Quality);
        Assert.Equal(0, new RecordingOptions(TestRegion, new RecordingOutput.Gif(GIFSettings.Standard), countdownSeconds: -4).CountdownSeconds);
        Assert.Equal(1920, MaxResolution.P1080.MaxLongestEdge());
        Assert.Null(MaxResolution.Original.MaxLongestEdge());
    }

    [Fact]
    [Unit]
    public void AfterRecordingComesFromSettingsUnlessTheTakeOverridesIt()
    {
        var defaults = new RecordingDefaults { AfterRecording = AfterRecordingAction.SaveSilently };
        var plain = RecordingOptions.Resolve(TestRegion, RecordingOutputKind.Video, defaults);
        Assert.Equal(AfterRecordingAction.SaveSilently, plain.AfterRecording);

        var studio = RecordingOptions.Resolve(
            TestRegion, RecordingOutputKind.Video, defaults, overrides: new RecordingOverrides(afterRecording: AfterRecordingAction.OpenEditor)
        );
        Assert.Equal(AfterRecordingAction.OpenEditor, studio.AfterRecording);
        Assert.Equal(plain.Microphone, studio.Microphone);
    }

    [Fact]
    [Unit]
    public void HidingNotificationsComesFromTheDefaultsAndDesktopIconsFromTheParameter()
    {
        var defaults = RecordingDefaults.Standard with { HideNotifications = true };
        var o = RecordingOptions.Resolve(TestRegion, RecordingOutputKind.Video, defaults, hideDesktopIcons: true);
        Assert.True(o.HideNotifications);
        Assert.True(o.HideDesktopIcons);
    }

    [Fact]
    [Unit]
    public void NothingIsHiddenByDefault()
    {
        var o = RecordingOptions.Resolve(TestRegion, RecordingOutputKind.Gif, RecordingDefaults.Standard);
        Assert.False(o.HideNotifications);
        Assert.False(o.HideDesktopIcons);
        Assert.False(RecordingDefaults.Standard.HideNotifications);
    }

    [Fact]
    [Unit]
    public void AStoredBlobFromBeforeHideNotificationsStillDecodes()
    {
        var defaults = RecordingDefaults.Standard with { HideNotifications = true };
        string json = JsonSerializer.Serialize(defaults);
        Assert.True(JsonSerializer.Deserialize<RecordingDefaults>(json)!.HideNotifications);

        var node = JsonSerializer.SerializeToNode(defaults)!.AsObject();
        node.Remove("HideNotifications");
        var older = node.Deserialize<RecordingDefaults>()!;
        Assert.False(older.HideNotifications);
    }
}
