// Ported from LightshotKit/Sources/LightshotKit/PinBoardController.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Lightshot.App.Views.QuickAccess;
using Lightshot.Core;
using Microsoft.Win32;
using Size = Lightshot.Core.Size;

namespace Lightshot.App.Views.Pin;

public partial class PinWindow : Window
{
    private const int WM_SIZING = 0x0214;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    private readonly CapturedImage _image;
    private readonly double _aspectRatio;
    private readonly BitmapSource _bitmap;

    public CapturedImage Image => _image;
    public BitmapSource Bitmap => _bitmap;
    public double AspectRatio => _aspectRatio;

    public Action? OnCopy { get; set; }
    public Func<bool>? OnSave { get; set; }
    public Func<bool>? OnSaveAs { get; set; }
    public Action? OnClose { get; set; }

    public PinWindow(CapturedImage image, Size initialSize)
    {
        _image = image;
        _aspectRatio = image.PixelHeight > 0 ? (double)image.PixelWidth / image.PixelHeight : 1.0;
        _bitmap = CardViewModel.CreateBitmapSource(image);

        DataContext = this;

        Width = initialSize.Width;
        Height = initialSize.Height;

        InitializeComponent();

        SourceInitialized += OnSourceInitialized;
        MouseEnter += (s, e) => ActionBar.Visibility = Visibility.Visible;
        MouseLeave += (s, e) => ActionBar.Visibility = Visibility.Collapsed;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        KeyDown += OnWindowKeyDown;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        // Apply DWM rounded corners
        int cornerPref = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));

        // Hook WM_SIZING for aspect-locked resizing
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_SIZING)
        {
            var edge = (SizingEdge)wParam.ToInt32();
            var rect = Marshal.PtrToStructure<RECT>(lParam);

            var adjusted = PinWindowMath.AdjustSizingRect(edge, rect.Left, rect.Top, rect.Right, rect.Bottom, _aspectRatio);
            rect.Left = adjusted.Left;
            rect.Top = adjusted.Top;
            rect.Right = adjusted.Right;
            rect.Bottom = adjusted.Bottom;

            Marshal.StructureToPtr(rect, lParam, true);
            handled = true;
            return (IntPtr)1;
        }
        return IntPtr.Zero;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch
            {
                // DragMove may throw if button released quickly
            }
        }
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ClosePin();
            e.Handled = true;
            return;
        }

        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl)
        {
            if (e.Key == Key.W)
            {
                ClosePin();
                e.Handled = true;
            }
            else if (e.Key == Key.C || e.Key == Key.S)
            {
                // Ctrl+S on a pin copies and the pin stays (SPECS 1.7, APP §3)
                CopyPin();
                e.Handled = true;
            }
        }
    }

    public void CopyPin()
    {
        if (OnCopy != null)
        {
            OnCopy();
        }
        else
        {
            DefaultCopy();
        }
    }

    public void SavePin()
    {
        if (OnSave != null)
        {
            OnSave();
        }
        else
        {
            DefaultSaveAs();
        }
    }

    public void SaveAsPin()
    {
        if (OnSaveAs != null)
        {
            OnSaveAs();
        }
        else
        {
            DefaultSaveAs();
        }
    }

    public void ClosePin()
    {
        OnClose?.Invoke();
        Close();
    }

    private void DefaultCopy()
    {
        try
        {
            Clipboard.SetImage(_bitmap);
        }
        catch
        {
            // Clipboard access can be transiently locked
        }
    }

    private void DefaultSaveAs()
    {
        var sfd = new SaveFileDialog
        {
            Filter = "PNG Image (*.png)|*.png",
            DefaultExt = ".png",
            FileName = $"Screenshot {DateTime.Now:yyyy-MM-dd at HH.mm.ss}.png"
        };
        if (sfd.ShowDialog(this) == true)
        {
            File.WriteAllBytes(sfd.FileName, _image.Data.ToArray());
        }
    }

    private void OnCopyClick(object sender, RoutedEventArgs e) => CopyPin();
    private void OnSaveClick(object sender, RoutedEventArgs e) => SavePin();
    private void OnCloseClick(object sender, RoutedEventArgs e) => ClosePin();

    private void OnMenuCopyClick(object sender, RoutedEventArgs e) => CopyPin();
    private void OnMenuSaveClick(object sender, RoutedEventArgs e) => SavePin();
    private void OnMenuSaveAsClick(object sender, RoutedEventArgs e) => SaveAsPin();
    private void OnMenuCloseClick(object sender, RoutedEventArgs e) => ClosePin();

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
