// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Controls;
using Lightshot.App.Views.Recording;
using Lightshot.App.Views.Settings;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.App.Tests;

public class SettingsWindowTests
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
        public bool LaunchAtLogin { get; set; } = false;
        public RecordingDefaults RecordingDefaults { get; set; } = new();
        public bool RememberLastRecordingArea { get; set; } = false;
        public CaptureRegion? LastRecordingRegion { get; set; }
        public AppearancePreference Appearance { get; set; } = AppearancePreference.System;
        public bool OcrKeepsLineBreaks { get; set; } = true;
        public bool HideDesktopIcons { get; set; } = false;
        public bool AdjustAreaBeforeCapture { get; set; } = false;
        public QuickAccessSettings QuickAccess { get; set; } = new();
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
    public void HasRecordingTabHostingRecordingPane()
    {
        RunInSta(() =>
        {
            var store = new MemorySettingsStore();
            var vm = new SettingsViewModel(store);
            var window = new SettingsWindow(vm);

            try
            {
                var grid = (Grid)window.Content;
                var tabControl = grid.Children.OfType<TabControl>().FirstOrDefault();
                Assert.NotNull(tabControl);

                var recordingTab = tabControl.Items.OfType<TabItem>().FirstOrDefault(t => t.Header?.ToString() == "Recording");
                Assert.NotNull(recordingTab);
                Assert.IsType<RecordingPane>(recordingTab.Content);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
