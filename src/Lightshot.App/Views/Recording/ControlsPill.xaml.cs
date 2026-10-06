using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Lightshot.Platform.Windows.Interop;

namespace Lightshot.App.Views.Recording;

public partial class ControlsPill : Window
{
    private const int GWL_EXSTYLE = -20;
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint WM_MOUSEACTIVATE = 0x0021;
    private const int MA_NOACTIVATE = 3;

    public ControlsPillViewModel? ViewModel => DataContext as ControlsPillViewModel;

    public ControlsPill()
    {
        InitializeComponent();
    }

    public ControlsPill(ControlsPillViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        // Apply WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST
        IntPtr curExStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        IntPtr newExStyle = (IntPtr)(curExStyle.ToInt64() | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, newExStyle);

        // Apply capture exclusion (WDA_EXCLUDEFROMCAPTURE)
        Win32Window.SetCaptureExclusion(hwnd, true);

        // Hook WndProc to intercept WM_MOUSEACTIVATE so clicking does not activate window
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == (int)WM_MOUSEACTIVATE)
        {
            handled = true;
            return (IntPtr)MA_NOACTIVATE;
        }
        return IntPtr.Zero;
    }

    public void PositionPill(bool top)
    {
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;

        Left = (screenWidth - ActualWidth) / 2.0;
        Top = top ? 24 : (screenHeight - ActualHeight - 48);
    }

    private void OnPauseResumeClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.PauseResume();
    }

    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.Stop();
    }

    private void OnRestartClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.Restart();
    }

    private void OnDiscardClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.Discard();
    }

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : SetWindowLong32(hWnd, nIndex, dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
