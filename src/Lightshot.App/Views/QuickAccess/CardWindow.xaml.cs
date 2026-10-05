// Ported from LightshotKit/Sources/LightshotKit/QuickAccess.swift and App/Sources/QuickAccessController.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using Lightshot.Core;
using Lightshot.Platform.Windows.Interop;
using Rect = Lightshot.Core.Rect;

namespace Lightshot.App.Views.QuickAccess;

public partial class CardWindow : Window, ICardWindow
{
    private const int GWL_EXSTYLE = -20;
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint WM_MOUSEACTIVATE = 0x0021;
    private const int MA_NOACTIVATE = 3;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    private readonly Guid _id;
    private readonly CapturedImage _image;
    private readonly CardViewModel _viewModel;
    private readonly Func<QuickAccessSettings> _settings;
    private System.Windows.Point? _mouseDownPoint;
    private string? _dragFilePath;
    private bool _isClosing;

    public Guid Id => _id;
    public CardViewModel ViewModel => _viewModel;

    public Rect Frame
    {
        get => new Rect(Left, Top, Width, Height);
        set
        {
            Left = value.MinX;
            Top = value.MinY;
            Width = value.Width;
            Height = value.Height;
        }
    }

    public Action<Guid, bool>? HoverChanged { get; set; }
    public Action<Guid>? RequestClose { get; set; }

    public CardWindow(
        Guid id,
        CapturedImage image,
        CardViewModel viewModel,
        Func<QuickAccessSettings>? settings = null)
    {
        _id = id;
        _image = image;
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _settings = settings ?? (() => new QuickAccessSettings());

        DataContext = _viewModel;

        Width = _viewModel.Size.Width;
        Height = _viewModel.Size.Height;

        InitializeComponent();

        SourceInitialized += OnSourceInitialized;
        MouseEnter += OnMouseEnter;
        MouseLeave += OnMouseLeave;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
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

        // Apply DWM rounded corners
        int cornerPref = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));

        // Hook WndProc for WM_MOUSEACTIVATE
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

    public void ShowCard(Rect initialFrame, Rect targetFrame, bool animate)
    {
        Frame = initialFrame;
        Show();

        if (animate)
        {
            var anim = new DoubleAnimation(initialFrame.MinX, targetFrame.MinX, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(LeftProperty, anim);
            Top = targetFrame.MinY;
            Width = targetFrame.Width;
            Height = targetFrame.Height;
        }
        else
        {
            Frame = targetFrame;
        }
    }

    public void CloseCard(bool animated = true)
    {
        if (_isClosing) return;
        _isClosing = true;

        if (animated)
        {
            var anim = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(150));
            anim.Completed += (s, e) => Close();
            BeginAnimation(OpacityProperty, anim);
        }
        else
        {
            Close();
        }
    }

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        _viewModel.IsHovering = true;
        HoverChanged?.Invoke(_id, true);
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        _viewModel.IsHovering = false;
        HoverChanged?.Invoke(_id, false);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _viewModel.Annotate();
            return;
        }
        _mouseDownPoint = e.GetPosition(this);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !_mouseDownPoint.HasValue) return;

        var current = e.GetPosition(this);
        var diff = current - _mouseDownPoint.Value;

        if (Math.Sqrt(diff.X * diff.X + diff.Y * diff.Y) > 4)
        {
            _mouseDownPoint = null;
            StartDragOut();
        }
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _mouseDownPoint = null;
    }

    private void StartDragOut()
    {
        try
        {
            if (_dragFilePath == null || !File.Exists(_dragFilePath))
            {
                string dir = Path.Combine(Path.GetTempPath(), "Lightshot Drag", _id.ToString());
                Directory.CreateDirectory(dir);
                _dragFilePath = Path.Combine(dir, $"{_id}.png");
                File.WriteAllBytes(_dragFilePath, _image.Data.ToArray());
            }

            var dataObj = new DataObject();
            dataObj.SetData(DataFormats.FileDrop, new string[] { _dragFilePath });
            dataObj.SetData(DataFormats.Bitmap, _viewModel.Bitmap);

            var effect = DragDrop.DoDragDrop(this, dataObj, DragDropEffects.Copy);
            bool altHeld = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
            HandleDragDropCompleted(effect, altHeld);
        }
        catch
        {
            // Ignore drag-drop cancellation
        }
    }

    internal void HandleDragDropCompleted(DragDropEffects effect, bool altHeld)
    {
        // ⌥ at drop inverts Close after dragging
        bool dropped = effect != DragDropEffects.None;
        bool closeAfter = _settings().CloseAfterDragging;
        bool keep = !closeAfter != altHeld;

        if (dropped && !keep)
        {
            RequestClose?.Invoke(_id);
        }
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        ConfirmAction(() => _viewModel.Copy());
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        ConfirmAction(() => _viewModel.Save());
    }

    private void ConfirmAction(Action action)
    {
        if (_viewModel.IsConfirmed) return;
        action();
        _viewModel.IsConfirmed = true;
        Task.Delay(600).ContinueWith(_ =>
        {
            Dispatcher.Invoke(() => RequestClose?.Invoke(_id));
        });
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => RequestClose?.Invoke(_id);
    private void OnPinClick(object sender, RoutedEventArgs e) => _viewModel.Pin();
    private void OnAnnotateClick(object sender, RoutedEventArgs e) => _viewModel.Annotate();

    private void OnMenuCopyClick(object sender, RoutedEventArgs e) => ConfirmAction(() => _viewModel.Copy());
    private void OnMenuSaveClick(object sender, RoutedEventArgs e) => ConfirmAction(() => _viewModel.Save());
    private void OnMenuSaveAsClick(object sender, RoutedEventArgs e) => _viewModel.SaveAs();
    private void OnMenuAnnotateClick(object sender, RoutedEventArgs e) => _viewModel.Annotate();
    private void OnMenuPinClick(object sender, RoutedEventArgs e) => _viewModel.Pin();
    private void OnMenuCloseClick(object sender, RoutedEventArgs e) => RequestClose?.Invoke(_id);
    private void OnMenuCloseAllClick(object sender, RoutedEventArgs e) => _viewModel.CloseAll();

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);
    }

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : SetWindowLong32(hWnd, nIndex, dwNewLong);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
