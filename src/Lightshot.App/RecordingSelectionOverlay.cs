// MIT License, Copyright (c) 2026 Viet Le

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

    public Task<CaptureRegion?> SelectRegionAsync(FrozenScreen? f, bool a) => inner.SelectRegionAsync(f, a);
    public Task<CaptureRegion?> SelectWindowAsync(FrozenScreen? f) => inner.SelectWindowAsync(f);

    public async Task<RecordingChoice?> SelectRecordingAsync(CaptureRegion? initial, RecordingDefaults defaults)
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
