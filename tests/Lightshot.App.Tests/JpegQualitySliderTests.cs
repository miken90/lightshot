// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Globalization;
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

[Trait("Tier", "Unit")]
public class JpegQualitySliderTests
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

    private static void WithSettingsFile(Action<string> body)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ls-proof-{Guid.NewGuid():N}");
        try { body(Path.Combine(dir, "settings.json")); }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private static SettingsViewModel FreshViewModel(string file) =>
        new(new JsonSettingsStore(file), isHevcAvailable: false);

    private static T Settled<T>(T pane) where T : FrameworkElement
    {
        pane.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        return pane;
    }

    private static void FindChildren<T>(DependencyObject root, List<T> results) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match) results.Add(match);
            FindChildren(child, results);
        }
    }

    private static List<RadioButton> Radios(DependencyObject root)
    {
        var found = new List<RadioButton>();
        FindChildren(root, found);
        return found;
    }

    private static RadioButton Radio(DependencyObject root, string content) =>
        Radios(root).Single(r => (r.Content as string) == content);

    private static Slider Slider(DependencyObject root)
    {
        var found = new List<Slider>();
        FindChildren(root, found);
        return found.Single();
    }

    private static TextBlock PercentLabel(DependencyObject root)
    {
        var found = new List<TextBlock>();
        FindChildren(root, found);
        return found.First(tb => tb.Text != null && tb.Text.EndsWith("%"));
    }

    [Fact]
    [Unit]
    public void JpegQualitySurvivesReopeningSettings()
    {
        WithSettingsFile(file =>
        {
            RunInSta(() =>
            {
                var pane = Settled(new ScreenshotsPane { DataContext = FreshViewModel(file) });
                var jpegRadio = Radio(pane, "JPEG");
                jpegRadio.IsChecked = true;
                Settled(pane);

                var slider = Slider(pane);
                Assert.True(slider.IsEnabled, "Slider should be enabled when JPEG format is selected");
                slider.Value = 75.0;
                Settled(pane);

                var vm = (SettingsViewModel)pane.DataContext;
                Assert.Equal(0.75, vm.JpegQuality, 2);
            });

            RunInSta(() =>
            {
                var reopened = Settled(new ScreenshotsPane { DataContext = FreshViewModel(file) });
                var vm = (SettingsViewModel)reopened.DataContext;

                Assert.True(vm.FormatIsJpeg, "FormatIsJpeg should be true after reopen");
                Assert.Equal(0.75, vm.JpegQuality, 2);

                var jpegRadio = Radio(reopened, "JPEG");
                Assert.True(jpegRadio.IsChecked == true, "JPEG radio should be checked after reopen");

                var slider = Slider(reopened);
                Assert.True(slider.IsEnabled, "Slider should be enabled after reopen");
                Assert.Equal(75.0, slider.Value, 1);

                var label = PercentLabel(reopened);
                Assert.Equal("75%", label.Text);
            });
        });
    }

    [Fact]
    [Unit]
    public void SliderIsDisabledWhenPngSelected()
    {
        WithSettingsFile(file =>
        {
            RunInSta(() =>
            {
                var pane = Settled(new ScreenshotsPane { DataContext = FreshViewModel(file) });
                var slider = Slider(pane);
                var pngRadio = Radio(pane, "PNG");
                var jpegRadio = Radio(pane, "JPEG");

                // Fresh settings file defaults to PNG format. Slider must be disabled.
                Assert.True(pngRadio.IsChecked == true, "PNG radio should be checked by default");
                Assert.False(slider.IsEnabled, "Slider should be disabled when PNG is selected by default");

                // Switching to JPEG enables the slider.
                jpegRadio.IsChecked = true;
                Settled(pane);
                Assert.True(slider.IsEnabled, "Slider should be enabled when JPEG is selected");

                // Switching back to PNG disables the slider again.
                pngRadio.IsChecked = true;
                Settled(pane);
                Assert.False(slider.IsEnabled, "Slider should be disabled when PNG is re-selected");
            });
        });
    }

    [Fact]
    [Unit]
    public void PercentConverterConvertsBothWaysCorrectly()
    {
        var converter = new PercentConverter();
        var culture = CultureInfo.InvariantCulture;

        // Convert (double 0..1 to percentage 10..100)
        Assert.Equal(90.0, (double)converter.Convert(0.9, typeof(double), null!, culture));
        Assert.Equal(75.0, (double)converter.Convert(0.75, typeof(double), null!, culture));
        Assert.Equal(10.0, (double)converter.Convert(0.1, typeof(double), null!, culture));
        Assert.Equal(100.0, (double)converter.Convert(1.0, typeof(double), null!, culture));
        Assert.Equal(90.0, (double)converter.Convert("not a double", typeof(double), null!, culture));

        // ConvertBack (percentage 10..100 to double 0.1..1.0 clamped)
        Assert.Equal(0.9, (double)converter.ConvertBack(90.0, typeof(double), null!, culture), 2);
        Assert.Equal(0.75, (double)converter.ConvertBack(75.0, typeof(double), null!, culture), 2);
        Assert.Equal(0.1, (double)converter.ConvertBack(10.0, typeof(double), null!, culture), 2);
        Assert.Equal(1.0, (double)converter.ConvertBack(100.0, typeof(double), null!, culture), 2);
        Assert.Equal(0.1, (double)converter.ConvertBack(5.0, typeof(double), null!, culture), 2);
        Assert.Equal(1.0, (double)converter.ConvertBack(150.0, typeof(double), null!, culture), 2);
        Assert.Equal(0.9, (double)converter.ConvertBack("not a double", typeof(double), null!, culture), 2);
    }
}
