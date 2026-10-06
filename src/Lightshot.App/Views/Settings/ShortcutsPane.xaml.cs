using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Lightshot.Core;

namespace Lightshot.App.Views.Settings;

public partial class ShortcutsPane : UserControl
{
    private bool _syncing;
    private SettingsViewModel? _vm;
    private (HotkeyRecorder Recorder, CaptureAction Action)[] Rows => new[] {
        (AreaRecorder, CaptureAction.Area),
        (FullscreenRecorder, CaptureAction.Fullscreen),
        (WindowRecorder, CaptureAction.Window),
        (RepeatLastRecorder, CaptureAction.RepeatLast),
        (RecordScreenRecorder, CaptureAction.RecordScreen),
        (PauseResumeRecorder, CaptureAction.PauseResumeRecording),
        (RestartRecorder, CaptureAction.RestartRecording)
    };

    public ShortcutsPane()
    {
        InitializeComponent();
        // Subscribe once per recorder; the handler reads the current _vm, so a DataContext swap needs no re-subscribe.
        var dpd = DependencyPropertyDescriptor.FromProperty(HotkeyRecorder.BindingProperty, typeof(HotkeyRecorder));
        foreach (var (rec, action) in Rows)
        {
            dpd.AddValueChanged(rec, (_, _) =>
            {
                if (!_syncing && _vm != null)
                {
                    _vm.SetBinding(action, rec.Binding);
                }
            });
        }
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm != null) _vm.PropertyChanged -= OnVmPropertyChanged;   // unsubscribe the old VM
        _vm = DataContext as SettingsViewModel;
        if (_vm == null) return;
        _vm.PropertyChanged += OnVmPropertyChanged;
        UpdateRecorders(_vm);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SettingsViewModel.Hotkeys) ||
            args.PropertyName == nameof(SettingsViewModel.Conflicts) ||
            args.PropertyName == nameof(SettingsViewModel.HasConflicts))
        {
            if (_vm != null) UpdateRecorders(_vm);
        }
    }

    private void UpdateRecorders(SettingsViewModel vm)
    {
        _syncing = true;
        try
        {
            foreach (var (rec, action) in Rows)
            {
                rec.Binding = vm.Hotkeys[action];
            }
            ConflictWarning.Visibility = vm.HasConflicts ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnResetDefaultsClick(object sender, RoutedEventArgs e)
    {
        if (_vm != null)
        {
            _syncing = true;
            try
            {
                _vm.ResetHotkeysToDefaults();
                UpdateRecorders(_vm);
            }
            finally
            {
                _syncing = false;
            }
        }
    }
}
