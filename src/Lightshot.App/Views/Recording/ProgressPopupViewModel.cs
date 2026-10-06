using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Lightshot.App.Views.Recording;

public class ProgressPopupViewModel : INotifyPropertyChanged
{
    private string _title = "Processing";
    private string _message = string.Empty;
    private double _progress;
    private bool _isIndeterminate;
    private bool _canCancelToVideo;
    private bool _isCancelled;
    private bool _cancelledToVideo;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? Cancelled;
    public event EventHandler? CancelToVideoRequested;

    public ProgressPopupViewModel(
        string title = "Processing",
        string message = "",
        bool canCancelToVideo = false,
        bool isIndeterminate = false)
    {
        _title = title;
        _message = message;
        _canCancelToVideo = canCancelToVideo;
        _isIndeterminate = isIndeterminate;
    }

    public string Title
    {
        get => _title;
        set
        {
            if (_title != value)
            {
                _title = value;
                OnPropertyChanged();
            }
        }
    }

    public string Message
    {
        get => _message;
        set
        {
            if (_message != value)
            {
                _message = value;
                OnPropertyChanged();
            }
        }
    }

    public double Progress
    {
        get => _progress;
        set
        {
            if (Math.Abs(_progress - value) > 0.01)
            {
                _progress = Math.Clamp(value, 0.0, 100.0);
                OnPropertyChanged();
            }
        }
    }

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        set
        {
            if (_isIndeterminate != value)
            {
                _isIndeterminate = value;
                OnPropertyChanged();
            }
        }
    }

    public bool CanCancelToVideo
    {
        get => _canCancelToVideo;
        set
        {
            if (_canCancelToVideo != value)
            {
                _canCancelToVideo = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsCancelled => _isCancelled;

    public bool CancelledToVideo => _cancelledToVideo;

    public void Cancel()
    {
        _isCancelled = true;
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    public void CancelToVideo()
    {
        _cancelledToVideo = true;
        CancelToVideoRequested?.Invoke(this, EventArgs.Empty);
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
