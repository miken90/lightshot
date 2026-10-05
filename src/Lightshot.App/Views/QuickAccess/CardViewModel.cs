// Ported from LightshotKit/Sources/LightshotKit/QuickAccess.swift and App/Sources/QuickAccessController.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lightshot.Core;

namespace Lightshot.App.Views.QuickAccess;

/// <summary>
/// View model for a single Quick Access overlay card.
/// </summary>
public class CardViewModel : INotifyPropertyChanged
{
    private bool _isHovering;
    private bool _isConfirmed;
    private BitmapSource? _bitmap;

    public Guid Id { get; }
    public CapturedImage Image { get; }
    public Size Size { get; }

    public BitmapSource Bitmap => _bitmap ??= CreateBitmapSource(Image);

    public bool IsHovering
    {
        get => _isHovering;
        set
        {
            if (_isHovering != value)
            {
                _isHovering = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsConfirmed
    {
        get => _isConfirmed;
        set
        {
            if (_isConfirmed != value)
            {
                _isConfirmed = value;
                OnPropertyChanged();
            }
        }
    }

    public Action? OnCopy { get; set; }
    public Action? OnSave { get; set; }
    public Action? OnSaveAs { get; set; }
    public Action? OnAnnotate { get; set; }
    public Action? OnPin { get; set; }
    public Action? OnClose { get; set; }
    public Action? OnCloseAll { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public CardViewModel(Guid id, CapturedImage image)
    {
        Id = id;
        Image = image;
        Size = QuickAccessLayout.CardSize(image.PixelWidth, image.PixelHeight);
    }

    public void Copy() => OnCopy?.Invoke();
    public void Save() => OnSave?.Invoke();
    public void SaveAs() => OnSaveAs?.Invoke();
    public void Annotate() => OnAnnotate?.Invoke();
    public void Pin() => OnPin?.Invoke();
    public void Close() => OnClose?.Invoke();
    public void CloseAll() => OnCloseAll?.Invoke();

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public static BitmapSource CreateBitmapSource(CapturedImage image)
    {
        byte[] bytes = image.Data.ToArray();
        if (bytes.Length == 0)
        {
            var empty = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
            empty.Freeze();
            return empty;
        }

        if (image.PixelWidth > 0 && image.PixelHeight > 0 && bytes.Length == image.PixelWidth * image.PixelHeight * 4)
        {
            var rawBs = BitmapSource.Create(
                image.PixelWidth,
                image.PixelHeight,
                96, 96,
                PixelFormats.Bgra32,
                null,
                bytes,
                image.PixelWidth * 4);
            rawBs.Freeze();
            return rawBs;
        }

        try
        {
            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = ms;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            int w = Math.Max(1, image.PixelWidth);
            int h = Math.Max(1, image.PixelHeight);
            var fallback = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, new byte[w * h * 4], w * 4);
            fallback.Freeze();
            return fallback;
        }
    }
}
