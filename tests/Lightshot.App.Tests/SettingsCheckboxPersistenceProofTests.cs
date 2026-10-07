// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using Lightshot.App.Views.Settings;
using Lightshot.Platform.Windows.Settings;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.App.Tests;

// Proof for the user report "Settings checkboxes do not keep their state": a checkbox whose
// IsChecked is a XAML literal has nothing to write to, so every reopen shows the literal again.
public class SettingsCheckboxPersistenceProofTests
{
    private static void RunInSta(Action action)
    {
        ExceptionDispatchInfo? captured = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { captured = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        captured?.Throw();
    }

    private static List<CheckBox> CheckBoxes(DependencyObject root)
    {
        var found = new List<CheckBox>();
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is CheckBox box) found.Add(box);
            found.AddRange(CheckBoxes(child));
        }
        return found;
    }

    [Fact]
    [Unit]
    public void EverySettingsCheckboxIsBoundToTheViewModel()
    {
        RunInSta(() =>
        {
            var vm = new SettingsViewModel(new JsonSettingsStore(Path.Combine(Path.GetTempPath(), $"ls-proof-{Guid.NewGuid():N}", "settings.json")), isHevcAvailable: false);
            var window = new SettingsWindow(vm);
            try
            {
                var unbound = CheckBoxes(window)
                    .Where(b => !BindingOperations.IsDataBound(b, ToggleButton.IsCheckedProperty))
                    .Select(b => b.Content?.ToString())
                    .ToList();

                Assert.True(unbound.Count == 0, "Unbound settings checkboxes: " + string.Join(" | ", unbound));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    [Unit]
    public void CopyToClipboardCheckboxSurvivesReopeningSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ls-proof-{Guid.NewGuid():N}");
        var file = Path.Combine(dir, "settings.json");
        try
        {
            RunInSta(() =>
            {
                var pane = new AfterCapturePane { DataContext = new SettingsViewModel(new JsonSettingsStore(file), isHevcAvailable: false) };
                pane.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var box = CheckBoxes(pane).Single(b => (b.Content as string) == "Copy image to clipboard");
                box.IsChecked = !(box.IsChecked ?? false);
                bool expected = box.IsChecked ?? false;

                var reopened = new AfterCapturePane { DataContext = new SettingsViewModel(new JsonSettingsStore(file), isHevcAvailable: false) };
                reopened.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var reopenedBox = CheckBoxes(reopened).Single(b => (b.Content as string) == "Copy image to clipboard");

                Assert.Equal(expected, reopenedBox.IsChecked ?? false);
            });
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
