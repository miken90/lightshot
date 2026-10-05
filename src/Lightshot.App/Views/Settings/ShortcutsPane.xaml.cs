using System.Windows;
using System.Windows.Controls;
using Lightshot.Core;

namespace Lightshot.App.Views.Settings;

public partial class ShortcutsPane : UserControl
{
    public ShortcutsPane()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            UpdateRecorders(vm);
            vm.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(SettingsViewModel.Hotkeys) ||
                    args.PropertyName == nameof(SettingsViewModel.Conflicts) ||
                    args.PropertyName == nameof(SettingsViewModel.HasConflicts))
                {
                    UpdateRecorders(vm);
                }
            };
        }
    }

    private void UpdateRecorders(SettingsViewModel vm)
    {
        AreaRecorder.Binding = vm.Hotkeys[CaptureAction.Area];
        FullscreenRecorder.Binding = vm.Hotkeys[CaptureAction.Fullscreen];
        WindowRecorder.Binding = vm.Hotkeys[CaptureAction.Window];
        RepeatLastRecorder.Binding = vm.Hotkeys[CaptureAction.RepeatLast];

        ConflictWarning.Visibility = vm.HasConflicts ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnResetDefaultsClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.ResetHotkeysToDefaults();
            UpdateRecorders(vm);
        }
    }
}
