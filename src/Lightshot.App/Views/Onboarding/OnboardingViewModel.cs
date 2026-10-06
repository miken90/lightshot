// Ported from App/Sources/PermissionOnboardingView.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Lightshot.Core;
using Lightshot.Platform.Windows.Hotkeys;
using Lightshot.Platform.Windows.Settings;

namespace Lightshot.App.Views.Onboarding;

/// <summary>
/// ViewModel driving the first-run onboarding checklist, PrintScreen claim detection,
/// and completion state ("app.onboarded").
/// </summary>
public class OnboardingViewModel : INotifyPropertyChanged
{
    private const string OnboardedKey = SettingsKeys.AppOnboarded;
    public const string KeyboardSettingsUri = "ms-settings:easeofaccess-keyboard";

    private readonly ISettingsStore _store;
    private readonly PermissionOnboardingModel _model;
    private readonly Func<bool> _checkPrintScreenClaim;
    private readonly Action _openKeyboardSettings;

    public event PropertyChangedEventHandler? PropertyChanged;

    public OnboardingViewModel(
        ISettingsStore store,
        PermissionOnboardingModel? model = null,
        Func<bool>? checkPrintScreenClaim = null,
        Action? openKeyboardSettings = null)
    {
        _store = store;
        _model = model ?? new PermissionOnboardingModel([]);
        _checkPrintScreenClaim = checkPrintScreenClaim ?? (() => PrintScreenClaim.GetSnippingToolSetting() == 1);
        _openKeyboardSettings = openKeyboardSettings ?? OpenKeyboardSettingsDefault;

        _model.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(PermissionOnboardingModel.IsSatisfied))
            {
                OnPropertyChanged(nameof(IsSatisfied));
            }
        };
    }

    public bool IsOnboarded => _store.GetSetting(OnboardedKey) == "true";

    public bool IsPrintScreenClaimedBySnippingTool => _checkPrintScreenClaim();

    public PermissionOnboardingModel PermissionModel => _model;

    public bool IsSatisfied => _model.IsSatisfied;

    public void CompleteOnboarding()
    {
        _store.SetSetting(OnboardedKey, "true");
        OnPropertyChanged(nameof(IsOnboarded));
    }

    public void OpenKeyboardSettings()
    {
        _openKeyboardSettings();
    }

    private static void OpenKeyboardSettingsDefault()
    {
        try
        {
            Process.Start(new ProcessStartInfo(KeyboardSettingsUri) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
