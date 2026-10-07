// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Lightshot.App.Views.Settings;
using Lightshot.Platform.Windows.Settings;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.App.Tests;

// Proof for the user report "Settings radio buttons do not keep their state": pick an option,
// open a fresh store and pane on the same settings file, and the same option must be checked.
public class SettingsRadioPersistenceProofTests
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

    private static List<RadioButton> Radios(DependencyObject root)
    {
        var found = new List<RadioButton>();
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is RadioButton radio) found.Add(radio);
            found.AddRange(Radios(child));
        }
        return found;
    }

    private static RadioButton Radio(DependencyObject root, string content) =>
        Radios(root).Single(r => (r.Content as string) == content);

    private static void WithSettingsFile(Action<string> body)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ls-proof-{Guid.NewGuid():N}");
        try { body(Path.Combine(dir, "settings.json")); }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private static SettingsViewModel FreshViewModel(string file) =>
        new(new JsonSettingsStore(file), isHevcAvailable: false);

    // WPF scopes a GroupName by visual root, and every pane outside a shown window has a null
    // root, so two such panes on one thread uncheck each other's radios. Reopening on a fresh
    // STA thread gives the reopened pane its own (thread-static) group registry, as a real
    // reopen of the Settings window effectively does.

    // A pane outside a window attaches its bindings on a later dispatcher pass; without this
    // pump a click only sets a local value and never reaches the view-model.
    private static T Settled<T>(T pane) where T : FrameworkElement
    {
        pane.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        return pane;
    }

    [Theory]
    [Unit]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void AppearanceRadioSurvivesReopeningSettings(string choice)
    {
        WithSettingsFile(file =>
        {
            RunInSta(() =>
            {
                var pane = Settled(new GeneralPane { DataContext = FreshViewModel(file) });
                Radio(pane, choice).IsChecked = true;
            });

            RunInSta(() =>
            {
                var reopened = Settled(new GeneralPane { DataContext = FreshViewModel(file) });
                Assert.True(Radio(reopened, choice).IsChecked == true, $"'{choice}' is not checked after reopening Settings");
            });
        });
    }

    [Fact]
    [Unit]
    public void AppearanceRadioShowsTheStoredValue()
    {
        WithSettingsFile(file => RunInSta(() =>
        {
            var pane = Settled(new GeneralPane { DataContext = FreshViewModel(file) });

            // A fresh store defaults to System, so exactly that radio must be checked.
            var checkedNames = Radios(pane).Where(r => r.GroupName == "Appearance" && r.IsChecked == true).Select(r => r.Content as string).ToList();
            Assert.Equal(new[] { "Match System" }, checkedNames);
        }));
    }

    [Theory]
    [Unit]
    [InlineData("JPEG")]
    [InlineData("PNG")]
    public void FormatRadioSurvivesReopeningSettings(string choice)
    {
        string other = choice == "PNG" ? "JPEG" : "PNG";
        WithSettingsFile(file =>
        {
            RunInSta(() =>
            {
                var pane = Settled(new ScreenshotsPane { DataContext = FreshViewModel(file) });
                // Move away from the choice first so the click under test is a real change.
                Radio(pane, other).IsChecked = true;
                Radio(pane, choice).IsChecked = true;
            });

            RunInSta(() =>
            {
                var reopened = Settled(new ScreenshotsPane { DataContext = FreshViewModel(file) });
                Assert.True(Radio(reopened, choice).IsChecked == true, $"'{choice}' is not checked after reopening Settings");
                Assert.False(Radio(reopened, other).IsChecked == true, "both format radios are checked");
            });
        });
    }
}
