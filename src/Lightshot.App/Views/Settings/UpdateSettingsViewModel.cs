// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Lightshot.Platform.Windows.Settings;
using Lightshot.Platform.Windows.Updates;

namespace Lightshot.App.Views.Settings;

public class UpdateSettingsViewModel : INotifyPropertyChanged
{
    private readonly ISettingsStore _store;
    private readonly Func<Task<UpdateCheckOutcome>>? _checkNow;
    private readonly Func<Task>? _restart;
    private bool _checking;
    private string _statusText = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CurrentVersion { get; }
    public bool IsInstalled { get; }
    public string? StagedVersion { get; private set; }
    public bool IsStaged => StagedVersion != null;

    public bool AutoCheck
    {
        get => !string.Equals(_store.GetSetting(SettingsKeys.UpdateEnabled), "false", StringComparison.OrdinalIgnoreCase);
        set
        {
            _store.SetSetting(SettingsKeys.UpdateEnabled, value ? "true" : "false");
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanCheckNow));
        }
    }

    public bool CanCheckNow => IsInstalled && AutoCheck && !_checking;

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText != value)
            {
                _statusText = value;
                OnPropertyChanged();
            }
        }
    }

    public UpdateSettingsViewModel(
        ISettingsStore store,
        string currentVersion,
        bool isInstalled,
        string? stagedVersion = null,
        Func<Task<UpdateCheckOutcome>>? checkNow = null,
        Func<Task>? restart = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        CurrentVersion = currentVersion;
        IsInstalled = isInstalled;
        StagedVersion = stagedVersion;
        _checkNow = checkNow;
        _restart = restart;

        if (!IsInstalled)
        {
            StatusText = "Updates are available in the installed app only.";
        }
        else if (IsStaged)
        {
            StatusText = $"Version {StagedVersion} is ready. It installs when you quit Lightshot.";
        }
    }

    public async Task CheckNowAsync()
    {
        if (!CanCheckNow || _checkNow == null) return;
        _checking = true;
        OnPropertyChanged(nameof(CanCheckNow));

        try
        {
            var outcome = await _checkNow().ConfigureAwait(true);
            StatusText = outcome switch
            {
                UpdateCheckOutcome.Staged => $"Version {StagedVersion} is ready. It installs when you quit Lightshot.",
                UpdateCheckOutcome.NoUpdate => $"Lightshot is up to date. Last checked {DateTime.Now:t}.",
                UpdateCheckOutcome.Disabled => "Automatic update checks are off.",
                UpdateCheckOutcome.NotInstalled => "Updates are available in the installed app only.",
                UpdateCheckOutcome.StageFailed => "The update could not be downloaded. Lightshot will try again later.",
                _ => string.Empty
            };
        }
        finally
        {
            _checking = false;
            OnPropertyChanged(nameof(CanCheckNow));
        }
    }

    public Task RestartAsync() => _restart?.Invoke() ?? Task.CompletedTask;

    public void SetStaged(string version)
    {
        StagedVersion = version;
        StatusText = $"Version {version} is ready. It installs when you quit Lightshot.";
        OnPropertyChanged(nameof(StagedVersion));
        OnPropertyChanged(nameof(IsStaged));
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
