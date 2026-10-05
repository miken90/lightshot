// Ported from LightshotKit/Tests/LightshotKitTests/PermissionOnboardingModelTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System.Collections.Generic;
using Lightshot.App.Views.Onboarding;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.App.Tests;

public class OnboardingViewModelTests
{
    private sealed class MemorySettingsStore : ISettingsStore
    {
        public ImageFormat DefaultFormat { get; set; } = new ImageFormat.Png();
        public string SaveLocation { get; set; } = @"C:\Test";
        public string FilenamePattern { get; set; } = "Screenshot";
        public HotkeyBindings Hotkeys { get; set; } = HotkeyBindings.Defaults;
        public bool OpenInEditor { get; set; } = true;
        public bool IncludeCursor { get; set; } = false;
        public double CaptureDelay { get; set; } = 0;
        public int HistoryRetention { get; set; } = 50;
        public bool LaunchAtLogin { get; set; } = false;
        public RecordingDefaults RecordingDefaults { get; set; } = new();
        public bool RememberLastRecordingArea { get; set; } = false;
        public CaptureRegion? LastRecordingRegion { get; set; }
        public AppearancePreference Appearance { get; set; } = AppearancePreference.System;
        public bool OcrKeepsLineBreaks { get; set; } = true;
        public bool HideDesktopIcons { get; set; } = false;
        public bool AdjustAreaBeforeCapture { get; set; } = false;
        public QuickAccessSettings QuickAccess { get; set; } = new();

        private readonly Dictionary<string, string?> _custom = new();
        public string? GetSetting(string key) => _custom.TryGetValue(key, out var v) ? v : null;
        public void SetSetting(string key, string? value) => _custom[key] = value;
    }

    [Fact]
    [Unit]
    public void CompletesOnceAndDetectsPrintScreenClaim()
    {
        var store = new MemorySettingsStore();
        bool snippingClaimed = true;
        bool keyboardSettingsOpened = false;

        var vm = new OnboardingViewModel(
            store,
            checkPrintScreenClaim: () => snippingClaimed,
            openKeyboardSettings: () => { keyboardSettingsOpened = true; });

        // 1. First run: not yet onboarded
        Assert.False(vm.IsOnboarded);

        // 2. Detects that PrintScreen is claimed by Snipping Tool
        Assert.True(vm.IsPrintScreenClaimedBySnippingTool);
        vm.OpenKeyboardSettings();
        Assert.True(keyboardSettingsOpened);
        Assert.Equal("ms-settings:easeofaccess-keyboard", OnboardingViewModel.KeyboardSettingsUri);

        // 3. User releases the claim
        snippingClaimed = false;
        Assert.False(vm.IsPrintScreenClaimedBySnippingTool);

        // 4. Complete onboarding
        vm.CompleteOnboarding();
        Assert.True(vm.IsOnboarded);
        Assert.Equal("true", store.GetSetting("app.onboarded"));

        // 5. Subsequent runs read standing completion (completes once)
        var secondRunVm = new OnboardingViewModel(
            store,
            checkPrintScreenClaim: () => false);
        Assert.True(secondRunVm.IsOnboarded);
    }
}
