// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using Lightshot.App.Views.Recording;
using Lightshot.Core;
using Lightshot.Platform.Windows.Displays;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.App.Tests;

public class RecordingToolbarViewModelTests
{
    [Fact]
    [Unit]
    public void ReturnStartsVideo()
    {
        var defaults = new RecordingDefaults();
        var vm = new RecordingToolbarViewModel(defaults, initialWidth: 1920, initialHeight: 1080);

        RecordingChoice? startedChoice = null;
        vm.RecordingStarted += (s, choice) => startedChoice = choice;

        var choice = vm.StartVideo();

        Assert.NotNull(startedChoice);
        Assert.Equal(RecordingOutputKind.Video, choice.Output);
        Assert.Equal(RecordingOutputKind.Video, startedChoice.Output);
        Assert.Equal(1920, choice.Region.RecordedArea switch
        {
            RecordedArea.SubArea sub => sub.Rect.Width,
            _ => 0
        });
    }

    [Fact]
    [Unit]
    public void AltReturnStartsGif()
    {
        var defaults = new RecordingDefaults();
        var vm = new RecordingToolbarViewModel(defaults, initialWidth: 800, initialHeight: 600);

        RecordingChoice? startedChoice = null;
        vm.RecordingStarted += (s, choice) => startedChoice = choice;

        var choice = vm.StartGif();

        Assert.NotNull(startedChoice);
        Assert.Equal(RecordingOutputKind.Gif, choice.Output);
        Assert.Equal(RecordingOutputKind.Gif, startedChoice.Output);
    }

    [Fact]
    [Unit]
    public void TogglesMapToRecordingOptions()
    {
        var defaults = new RecordingDefaults
        {
            RecordMicrophone = false,
            RecordComputerAudio = false,
            HighlightClicks = false,
            ShowKeystrokes = false
        };

        var vm = new RecordingToolbarViewModel(defaults, initialWidth: 1280, initialHeight: 720);

        // Turn on toggles
        vm.MicrophoneEnabled = true;
        vm.ComputerAudioEnabled = true;
        vm.HighlightClicksEnabled = true;
        vm.ShowKeystrokesEnabled = true;
        vm.SelectedAudioInputId = "mic_usb_01";

        var options = vm.ResolveOptions();

        Assert.True(options.Microphone.IsOn);
        Assert.Equal("mic_usb_01", (options.Microphone as InputDeviceSelection.Device)?.Id);
        Assert.True(options.ComputerAudio);
        Assert.True(options.HighlightClicks);
        Assert.True(options.ShowKeystrokes);
    }

    [Fact]
    [Unit]
    public void HevcHiddenWhenUnavailable()
    {
        var vmUnavailable = new RecordingToolbarViewModel(isHevcAvailable: false);
        Assert.False(vmUnavailable.IsHevcAvailable);

        var vmAvailable = new RecordingToolbarViewModel(isHevcAvailable: true);
        Assert.True(vmAvailable.IsHevcAvailable);
    }

    [Fact]
    [Unit]
    public void WarningBadgesWhenMicrophoneIsMuted()
    {
        var vm = new RecordingToolbarViewModel();
        vm.MicrophoneEnabled = true;
        vm.IsMicrophoneMuted = false;
        Assert.False(vm.HasMicrophoneWarning);

        vm.IsMicrophoneMuted = true;
        Assert.True(vm.HasMicrophoneWarning);

        // Disabling mic hides the warning
        vm.MicrophoneEnabled = false;
        Assert.False(vm.HasMicrophoneWarning);
    }

    // Fullscreen fills the monitor the selection is on, in physical pixels: on a 150% primary the
    // old WPF PrimaryScreenWidth gave 1706 x 1066 DIPs at (0, 0) whatever monitor was selected.
    [Fact]
    [Unit]
    public void FullscreenFillsTheSelectedMonitorInPhysicalPixels()
    {
        var primary = new DisplayInfo(1, @"\\.\DISPLAY1", (IntPtr)1, new Rect(0, 0, 2560, 1600), new Rect(0, 0, 2560, 1552), 1.5, 144, 144, true, 7);
        var right = new DisplayInfo(2, @"\\.\DISPLAY5", (IntPtr)2, new Rect(2560, 0, 2532, 1170), new Rect(2560, 0, 2532, 1122), 1.0, 96, 96, false, 7);
        var vm = new RecordingToolbarViewModel(new RecordingDefaults(), initialWidth: 400, initialHeight: 300)
        {
            RegionX = 3626,
            RegionY = 435,
        };

        vm.SetFullscreen([primary, right]);

        Assert.Equal(2560, vm.RegionX);
        Assert.Equal(0, vm.RegionY);
        Assert.Equal(2532, vm.RegionWidth);
        Assert.Equal(1170, vm.RegionHeight);

        vm.RegionX = 100;
        vm.RegionY = 100;
        vm.SetFullscreen([primary, right]);
        Assert.Equal((0d, 0d, 2560, 1600), (vm.RegionX, vm.RegionY, vm.RegionWidth, vm.RegionHeight));
    }
}
