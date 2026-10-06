// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Lightshot.App.Views.Editor;
using Lightshot.App.Views.Notices;
using Lightshot.App.Views.Settings;
using Lightshot.App.Views.VideoEditor;
using Lightshot.Platform.Windows.Updates;
using Lightshot.Platform.Windows.Windows;

namespace Lightshot.App;

public sealed partial class AppController
{
    private UpdateService? _updates;
    private UpdateNoticeController? _updateNotice;
    private UpdateSettingsViewModel? _openUpdateSettings;

    private void InitializeUpdates()
    {
        _updates = new UpdateService(_settingsStore, new VelopackApplier(s => Trace.WriteLine(s)), AppPaths.Updates,
            logger: s => Trace.WriteLine(s));
        _updateNotice = new UpdateNoticeController(_updates, HasWorkInProgress,
            setTrayUpdate: text => _trayIcon.SetUpdateAvailable(text),
            showNotice: (title, text) => _trayIcon.ShowNotice(title, text),
            shutdown: () => Application.Current?.Dispatcher.BeginInvoke(new Action(() => Application.Current.Shutdown(0))),
            testMode: IsTestMode());
        _updateNotice.Staged += v => _openUpdateSettings?.SetStaged(v);
        _trayIcon.OnRestartToUpdate = () => _ = _updateNotice.RestartToUpdateAsync();
        _trayIcon.OnNoticeClicked = () => ShowSettingsWindow();
        _ = _updateNotice.StartAsync(); // StartAsync catches check failures itself
    }

    private UpdateSettingsViewModel CreateUpdateSettingsViewModel()
    {
        var vm = new UpdateSettingsViewModel(
            _settingsStore,
            _updates?.RunningVersion ?? AppVersion(),
            isInstalled: _updates?.IsInstalled == true && !IsTestMode(),
            stagedVersion: _updateNotice?.StagedVersion,
            checkNow: () => _updateNotice?.CheckNowAsync() ?? Task.FromResult(UpdateCheckOutcome.NotInstalled),
            restart: async () =>
            {
                if (_updateNotice != null) await _updateNotice.RestartToUpdateAsync();
            });
        _openUpdateSettings = vm;
        return vm;
    }

    private static string AppVersion() =>
        typeof(AppController).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private bool HasWorkInProgress()
    {
        // AppCoordinator.HasWorkInProgress (AppCoordinator.cs:51) covers recording, take start/finish, GIF conversion and archiving.
        if (_coordinator.HasWorkInProgress || _gifPopup != null || _prepPopup != null) return true;
        var windows = Application.Current?.Windows.OfType<Window>() ?? Enumerable.Empty<Window>();
        return windows.Any(w =>
            (w is EditorWindow { ViewModel.CanUndo: true }) ||
            (w is VideoEditorWindow { ViewModel: { } vm } && (vm.IsModified || vm.IsBusy)));
    }

    public void ApplyPendingUpdateOnExit()
    {
        try
        {
            _updateNotice?.ApplyOnExit();
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Apply on exit failed: {ex}");
        }
    }

    private void DisposeUpdates()
    {
        _updateNotice?.Dispose();
        _updates?.Dispose();
    }
}
