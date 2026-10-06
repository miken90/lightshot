// Ported from LightshotKit/Sources/LightshotKit/InputEventSource.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace Lightshot.Platform.Windows.Notifications;

public enum UserNotificationState
{
    NotPresent = 1,
    Busy = 2,
    RunningD3dFullScreen = 3,
    PresentationMode = 4,
    AcceptsNotifications = 5,
    QuietTime = 6,
    App = 7
}

public interface IUserNotificationStateProvider
{
    UserNotificationState QueryState();
}

public sealed class NotificationStateProbe
{
    public const string SettingsUri = "ms-settings:notifications";

    private readonly IUserNotificationStateProvider _provider;

    public NotificationStateProbe(IUserNotificationStateProvider? provider = null)
    {
        _provider = provider ?? new WindowsNotificationStateProvider();
    }

    public UserNotificationState CurrentState => _provider.QueryState();

    public bool ShouldShowGuidance(bool hideNotificationsEnabled) =>
        ShouldShowGuidance(hideNotificationsEnabled, CurrentState);

    public static bool ShouldShowGuidance(bool hideNotificationsEnabled, UserNotificationState state) =>
        hideNotificationsEnabled && state == UserNotificationState.AcceptsNotifications;

    private sealed class WindowsNotificationStateProvider : IUserNotificationStateProvider
    {
        public UserNotificationState QueryState()
        {
            try
            {
                var hr = PInvoke.SHQueryUserNotificationState(out var quns);
                if (hr.Value == 0) // S_OK
                {
                    return (UserNotificationState)(int)quns;
                }
            }
            catch
            {
                // Degrade gracefully
            }
            return UserNotificationState.AcceptsNotifications;
        }
    }
}
