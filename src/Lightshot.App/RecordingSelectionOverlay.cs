// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Lightshot.App.Views.Recording;
using Lightshot.Core;
using Lightshot.Platform.Windows.Audio;
using Lightshot.Platform.Windows.Notifications;

namespace Lightshot.App;

public sealed class RecordingSelectionOverlay(IOverlayController inner, AudioDeviceService audio, ISettingsStore settings) : IOverlayController
{
    private bool _bannerShown; // once per app session; detection is SHQueryUserNotificationState only (NotificationStateProbe.cs:51), no WNF or registry probe

    // 1 while any selection (area, window, recording) is open. A further trigger, e.g. a double click on a
    // hub command, would stack a second overlay, so it cancels instead. Recording stays out of this: only
    // the selection holds the flag, and a toggle during an active recording still stops it.
    private int _selecting;

    public Task<CaptureRegion?> SelectRegionAsync(FrozenScreen? f, bool a) => SelectOnceAsync(() => inner.SelectRegionAsync(f, a));
    public Task<CaptureRegion?> SelectWindowAsync(FrozenScreen? f) => SelectOnceAsync(() => inner.SelectWindowAsync(f));

    public Task<RecordingChoice?> SelectRecordingAsync(CaptureRegion? initial, RecordingDefaults defaults) =>
        SelectOnceAsync(() => SelectRecordingCoreAsync(initial, defaults));

    private async Task<T?> SelectOnceAsync<T>(Func<Task<T?>> select) where T : class
    {
        if (Interlocked.Exchange(ref _selecting, 1) == 1) return null;
        try { return await select(); }
        finally { Volatile.Write(ref _selecting, 0); }
    }

    private async Task<RecordingChoice?> SelectRecordingCoreAsync(CaptureRegion? initial, RecordingDefaults defaults)
    {
        var picked = await inner.SelectRecordingAsync(initial, defaults);
        if (picked == null) return null;
        var inputs = await audio.AvailableInputsAsync();
        bool banner = !_bannerShown && new NotificationStateProbe().ShouldShowGuidance(defaults.HideNotifications);
        _bannerShown |= banner;
        return await Application.Current.Dispatcher.InvokeAsync(
            () => RecordingToolbarWindow.ShowAsync(picked.Region, defaults, inputs, banner, settings)).Task.Unwrap();
    }
}
