using System;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace Lightshot.App.Views.Recording;

public partial class MediaViewerWindow : Window
{
    private readonly string _filePath;
    private bool _isPlaying = true;

    public MediaViewerWindow()
    {
        InitializeComponent();
        _filePath = string.Empty;
    }

    public MediaViewerWindow(string filePath) : this()
    {
        _filePath = filePath;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_filePath) && File.Exists(_filePath))
        {
            MediaPlayer.Source = new Uri(_filePath, UriKind.Absolute);
            MediaPlayer.Play();
            _isPlaying = true;
        }
    }

    private void OnMediaEnded(object sender, RoutedEventArgs e)
    {
        MediaPlayer.Position = TimeSpan.Zero;
        MediaPlayer.Play();
    }

    private void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        TogglePlayPause();
    }

    private void TogglePlayPause()
    {
        if (_isPlaying)
        {
            MediaPlayer.Pause();
            _isPlaying = false;
            PlayPauseIcon.Text = "\uE768";
        }
        else
        {
            MediaPlayer.Play();
            _isPlaying = true;
            PlayPauseIcon.Text = "\uE769";
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Space)
        {
            TogglePlayPause();
            e.Handled = true;
        }
    }
}
