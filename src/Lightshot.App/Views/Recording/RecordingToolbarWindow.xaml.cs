// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Lightshot.Core;

namespace Lightshot.App.Views.Recording;

public partial class RecordingToolbarWindow : Window
{
    public RecordingToolbarWindow()
    {
        InitializeComponent();
    }

    public static Task<RecordingChoice?> ShowAsync(
        CaptureRegion region,
        RecordingDefaults d,
        IReadOnlyList<AudioInputDevice> inputs,
        bool banner,
        ISettingsStore settings)
    {
        var tcs = new TaskCompletionSource<RecordingChoice?>();
        var window = new RecordingToolbarWindow();

        Lightshot.Core.Rect rect = region switch
        {
            CaptureRegion.RectRegion r => r.Rect,
            CaptureRegion.WindowRegion w => w.Frame,
            _ => new Lightshot.Core.Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight)
        };

        var vm = new RecordingToolbarViewModel(d, inputs, null, (int)rect.Width, (int)rect.Height);
        vm.RegionX = rect.X;
        vm.RegionY = rect.Y;
        window.Toolbar.DataContext = vm;

        if (banner)
        {
            window.Banner.Visibility = Visibility.Visible;
            window.Banner.Dismissed += (_, _) => window.Banner.Visibility = Visibility.Collapsed;
        }
        else
        {
            window.Banner.Visibility = Visibility.Collapsed;
        }

        vm.RecordingStarted += (_, c) =>
        {
            bool rectUnchanged = Math.Abs(vm.RegionX - rect.X) < 0.001 &&
                                 Math.Abs(vm.RegionY - rect.Y) < 0.001 &&
                                 vm.RegionWidth == (int)rect.Width &&
                                 vm.RegionHeight == (int)rect.Height;

            var choice = rectUnchanged ? (c with { Region = region }) : c;
            settings.RecordingDefaults = settings.RecordingDefaults with
            {
                CountdownEnabled = vm.CountdownEnabled,
                CountdownSeconds = vm.CountdownSeconds
            };

            tcs.TrySetResult(choice);
            window.Close();
        };

        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                tcs.TrySetResult(null);
                window.Close();
                e.Handled = true;
            }
        };

        window.Closed += (_, _) =>
        {
            tcs.TrySetResult(null);
        };

        void UpdatePosition()
        {
            var workArea = SystemParameters.WorkArea;
            window.Left = workArea.Left + (workArea.Width - window.ActualWidth) / 2;
            window.Top = workArea.Bottom - window.ActualHeight - 24;
        }

        window.SizeChanged += (_, _) => UpdatePosition();

        window.Loaded += (_, _) =>
        {
            UpdatePosition();
            window.Activate();
            window.Toolbar.Focus();
        };

        window.Show();
        return tcs.Task;
    }
}
