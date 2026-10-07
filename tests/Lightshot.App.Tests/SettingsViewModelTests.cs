// Ported from LightshotKit/Tests/LightshotKitTests/SettingsModelTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System.Collections.Generic;
using System.Linq;
using Lightshot.App.Views.Settings;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.App.Tests;

public class SettingsViewModelTests
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
        public int MagnifierZoom { get; set; } = 4;
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
    public void MagnifierZoomUpdatesStore()
    {
        var store = new MemorySettingsStore();
        var vm = new SettingsViewModel(store, applyHotkeys: _ => []);
        Assert.Equal(4, vm.MagnifierZoom);

        vm.MagnifierZoom = 8;

        Assert.Equal(8, store.MagnifierZoom);
    }

    [Fact]
    [Unit]
    public void ShortcutConflictsAreReported()
    {
        var store = new MemorySettingsStore();
        var refusedByOs = new List<CaptureAction>();
        var vm = new SettingsViewModel(
            store,
            applyHotkeys: bindings => refusedByOs.ToList());

        // 1. Initial state has default bindings, which do not conflict
        Assert.False(vm.HasConflicts);
        Assert.Empty(vm.Conflicts);
        Assert.Empty(vm.UnregisterableActions);

        // 2. Assign the same chord to two different actions -> conflict reported
        var sharedChord = new HotkeyBinding(0x2C, HotkeyModifiers.None, "PrintScreen");
        vm.SetBinding(CaptureAction.Area, sharedChord);
        vm.SetBinding(CaptureAction.Fullscreen, sharedChord);

        Assert.True(vm.HasConflicts);
        Assert.Single(vm.Conflicts);
        var conflict = vm.Conflicts[0];
        Assert.True(conflict.Binding.ChordEquals(sharedChord));
        Assert.Contains(CaptureAction.Area, conflict.Actions);
        Assert.Contains(CaptureAction.Fullscreen, conflict.Actions);

        // 3. Reassign one action to a unique chord -> conflict resolved
        var distinctChord = new HotkeyBinding(0x2C, HotkeyModifiers.Control, "PrintScreen");
        vm.SetBinding(CaptureAction.Fullscreen, distinctChord);

        Assert.False(vm.HasConflicts);
        Assert.Empty(vm.Conflicts);

        // 4. OS refuses chord registration -> reported in UnregisterableActions
        refusedByOs.Add(CaptureAction.Area);
        vm.SetBinding(CaptureAction.Area, sharedChord); // triggers applyHotkeys

        Assert.Contains(CaptureAction.Area, vm.UnregisterableActions);

        // 5. Reserved chords are flagged
        var winL = new HotkeyBinding('L', HotkeyModifiers.Command, "L");
        Assert.True(SettingsViewModel.IsReservedChord(winL));

        var normal = new HotkeyBinding(0x2C, HotkeyModifiers.Control, "PrintScreen");
        Assert.False(SettingsViewModel.IsReservedChord(normal));
    }
}
