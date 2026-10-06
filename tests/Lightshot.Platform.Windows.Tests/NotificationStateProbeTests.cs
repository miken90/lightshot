using Lightshot.Platform.Windows.Notifications;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class NotificationStateProbeTests
{
    private sealed class FakeNotificationStateProvider : IUserNotificationStateProvider
    {
        public UserNotificationState State { get; set; } = UserNotificationState.AcceptsNotifications;

        public UserNotificationState QueryState() => State;
    }

    [Fact]
    [Unit]
    public void MapsQunsStatesToGuidance()
    {
        var fake = new FakeNotificationStateProvider();
        var probe = new NotificationStateProbe(fake);

        // When hideNotifications is disabled, guidance is never shown regardless of state
        foreach (UserNotificationState state in Enum.GetValues<UserNotificationState>())
        {
            fake.State = state;
            Assert.False(probe.ShouldShowGuidance(hideNotificationsEnabled: false));
            Assert.False(NotificationStateProbe.ShouldShowGuidance(false, state));
        }

        // When hideNotifications is enabled:
        // AcceptsNotifications -> guidance should be shown so user can enable Focus/DND
        fake.State = UserNotificationState.AcceptsNotifications;
        Assert.True(probe.ShouldShowGuidance(hideNotificationsEnabled: true));
        Assert.True(NotificationStateProbe.ShouldShowGuidance(true, UserNotificationState.AcceptsNotifications));

        // QuietTime (Focus / DND active) -> guidance not needed
        fake.State = UserNotificationState.QuietTime;
        Assert.False(probe.ShouldShowGuidance(hideNotificationsEnabled: true));
        Assert.False(NotificationStateProbe.ShouldShowGuidance(true, UserNotificationState.QuietTime));

        // Busy / FullScreen / Presentation -> notifications suppressed, guidance not needed
        fake.State = UserNotificationState.Busy;
        Assert.False(probe.ShouldShowGuidance(hideNotificationsEnabled: true));

        fake.State = UserNotificationState.RunningD3dFullScreen;
        Assert.False(probe.ShouldShowGuidance(hideNotificationsEnabled: true));

        fake.State = UserNotificationState.PresentationMode;
        Assert.False(probe.ShouldShowGuidance(hideNotificationsEnabled: true));

        fake.State = UserNotificationState.NotPresent;
        Assert.False(probe.ShouldShowGuidance(hideNotificationsEnabled: true));

        fake.State = UserNotificationState.App;
        Assert.False(probe.ShouldShowGuidance(hideNotificationsEnabled: true));
    }

    [Fact]
    [Unit]
    public void SettingsUriPointsToNotificationSettings()
    {
        Assert.Equal("ms-settings:notifications", NotificationStateProbe.SettingsUri);
    }
}
