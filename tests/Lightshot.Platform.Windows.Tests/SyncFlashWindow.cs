// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Lightshot.Platform.Windows.Tests;

/// <summary>
/// Topmost borderless window placed at the desktop origin with a flash panel
/// that turns bright white for 100 ms at each scheduled clap instant.
/// </summary>
public sealed class SyncFlashWindow : IDisposable
{
    private readonly long[] _clapTicks;
    private readonly long _flashDurationTicks;
    private Window? _window;
    private Border? _flashBorder;
    private Thread? _thread;
    private bool _disposed;

    public SyncFlashWindow(long[] clapTicks, int flashMs = 100)
    {
        _clapTicks = clapTicks;
        _flashDurationTicks = (long)(flashMs / 1000.0 * Stopwatch.Frequency);
    }

    public void Start()
    {
        var ready = new ManualResetEventSlim(false);
        _thread = new Thread(() =>
        {
            _flashBorder = new Border
            {
                Width = 400,
                Height = 200,
                Background = Brushes.Black
            };

            _window = new Window
            {
                Title = "AudioSyncFlashSource",
                Width = 400,
                Height = 200,
                Left = 0,
                Top = 0,
                Topmost = true,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Background = Brushes.Black,
                Content = _flashBorder
            };

            CompositionTarget.Rendering += OnRendering;
            _window.Show();
            ready.Set();
            System.Windows.Threading.Dispatcher.Run();
        });

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.IsBackground = true;
        _thread.Start();

        if (!ready.Wait(10_000)) throw new TimeoutException("SyncFlashWindow failed to start.");
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        long now = Stopwatch.GetTimestamp();
        bool isFlashing = false;

        for (int i = 0; i < _clapTicks.Length; i++)
        {
            long start = _clapTicks[i];
            if (now >= start && now <= start + _flashDurationTicks)
            {
                isFlashing = true;
                break;
            }
        }

        if (_flashBorder != null)
        {
            _flashBorder.Background = isFlashing ? Brushes.White : Brushes.Black;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_window != null)
        {
            _window.Dispatcher.InvokeShutdown();
        }
    }
}
