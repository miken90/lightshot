// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using Lightshot.App.Views.Settings;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.App.Tests;

public class ShortcutsPaneTests
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

    private static void RunInSta(Action action)
    {
        ExceptionDispatchInfo? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        captured?.Throw();
    }

    [Fact]
    [Unit]
    public void RecordingShortcutRowsPersistThroughSetBinding()
    {
        RunInSta(() =>
        {
            var store = new MemorySettingsStore();
            var vm = new SettingsViewModel(store);
            var pane = new ShortcutsPane { DataContext = vm };

            var recordScreenRecorder = (HotkeyRecorder)pane.FindName("RecordScreenRecorder");
            Assert.NotNull(recordScreenRecorder);

            var newBinding = new HotkeyBinding(0x52, HotkeyModifiers.Control | HotkeyModifiers.Shift, "Ctrl+Shift+R");
            recordScreenRecorder.Binding = newBinding;

            Assert.Equal(newBinding, store.Hotkeys[CaptureAction.RecordScreen]);

            var areaRecorder = (HotkeyRecorder)pane.FindName("AreaRecorder");
            Assert.NotNull(areaRecorder);

            var areaBinding = new HotkeyBinding(0x41, HotkeyModifiers.Control | HotkeyModifiers.Shift, "Ctrl+Shift+A");
            areaRecorder.Binding = areaBinding;

            Assert.Equal(areaBinding, store.Hotkeys[CaptureAction.Area]);

            areaRecorder.Binding = null;
            Assert.False(store.Hotkeys.Assignments.ContainsKey(CaptureAction.Area));
        });
    }
}
