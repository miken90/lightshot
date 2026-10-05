// Ported from LightshotKit/Sources/LightshotKit/Theme.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using Lightshot.Core;

namespace Lightshot.App.Theming;

/// <summary>
/// Manages application-wide theming, system theme synchronization,
/// Fluent ThemeMode, DynamicResource theme dictionaries, and DWM window dark mode.
/// </summary>
public sealed class ThemeService : IDisposable
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    private static ThemeService? s_instance;
    public static ThemeService Instance => s_instance ??= new ThemeService();

    private AppearancePreference _preference = AppearancePreference.System;
    private Appearance _currentAppearance = Appearance.Light;
    private ResourceDictionary? _currentThemeDict;
    private bool _disposed;

    public AppearancePreference CurrentPreference => _preference;
    public Appearance CurrentAppearance => _currentAppearance;
    public bool IsDark => _currentAppearance == Appearance.Dark;

    public event Action<Appearance>? ThemeChanged;

    public ThemeService()
    {
        try
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }
        catch
        {
            // Non-interactive or test environment
        }

        _currentAppearance = _preference.Resolved(GetSystemAppearance());
    }

    /// <summary>
    /// Reads Windows system apps theme preference from registry (AppsUseLightTheme).
    /// </summary>
    public static Appearance GetSystemAppearance()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int val)
            {
                return val == 0 ? Appearance.Dark : Appearance.Light;
            }
        }
        catch
        {
        }
        return Appearance.Light;
    }

    /// <summary>
    /// Reads Windows taskbar/system theme preference from registry (SystemUsesLightTheme).
    /// </summary>
    public static bool GetSystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("SystemUsesLightTheme") is int val)
            {
                return val != 0;
            }
        }
        catch
        {
        }
        return false; // Default to dark taskbar
    }

    /// <summary>
    /// Applies the appearance preference app-wide.
    /// </summary>
    public void Apply(AppearancePreference preference)
    {
        _preference = preference;
        var resolved = preference.Resolved(GetSystemAppearance());
        _currentAppearance = resolved;

        ApplyToApplication();
        ThemeChanged?.Invoke(resolved);
    }

    private void ApplyToApplication()
    {
        if (Application.Current == null) return;

        if (Application.Current.Dispatcher.CheckAccess())
        {
            ApplyCore();
        }
        else
        {
            Application.Current.Dispatcher.Invoke(ApplyCore);
        }
    }

    private void ApplyCore()
    {
        if (Application.Current == null) return;

#pragma warning disable WPF0001
        try
        {
            Application.Current.ThemeMode = _preference switch
            {
                AppearancePreference.Light => ThemeMode.Light,
                AppearancePreference.Dark => ThemeMode.Dark,
                _ => ThemeMode.System
            };
        }
        catch
        {
            // ThemeMode fallback if not supported on runtime host
        }
#pragma warning restore WPF0001

        // Swap theme resource dictionary in Application.Current.Resources
        var appResources = Application.Current.Resources;
        if (_currentThemeDict != null)
        {
            appResources.MergedDictionaries.Remove(_currentThemeDict);
        }

        string themeUri = _currentAppearance == Appearance.Dark
            ? "pack://application:,,,/Lightshot.App;component/Theming/Themes/Dark.xaml"
            : "pack://application:,,,/Lightshot.App;component/Theming/Themes/Light.xaml";

        try
        {
            _currentThemeDict = new ResourceDictionary { Source = new Uri(themeUri, UriKind.Absolute) };
        }
        catch
        {
            // Fallback to programmatic dictionary via PaletteBridge
            _currentThemeDict = PaletteBridge.CreateThemeDictionary(_currentAppearance);
        }

        appResources.MergedDictionaries.Add(_currentThemeDict);

        // Update all existing windows
        foreach (Window window in Application.Current.Windows)
        {
            ApplyToWindow(window);
        }
    }

    /// <summary>
    /// Sets DWMWA_USE_IMMERSIVE_DARK_MODE on the specified window.
    /// </summary>
    public void ApplyToWindow(Window window)
    {
        if (window == null) return;

        bool dark = IsDark;
        var helper = new WindowInteropHelper(window);
        IntPtr hwnd = helper.Handle;

        if (hwnd == IntPtr.Zero)
        {
            EventHandler onSourceInitialized = null!;
            onSourceInitialized = (s, e) =>
            {
                window.SourceInitialized -= onSourceInitialized;
                SetDwmDarkMode(new WindowInteropHelper(window).Handle, dark);
            };
            window.SourceInitialized += onSourceInitialized;
        }
        else
        {
            SetDwmDarkMode(hwnd, dark);
        }
    }

    /// <summary>
    /// Alias for ApplyToWindow.
    /// </summary>
    public void ApplyWindowTheme(Window window) => ApplyToWindow(window);


    private static void SetDwmDarkMode(IntPtr hwnd, bool isDark)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            int value = isDark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        }
        catch
        {
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Window)
        {
            if (_preference == AppearancePreference.System)
            {
                Apply(AppearancePreference.System);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }
        catch
        {
        }
    }
}
