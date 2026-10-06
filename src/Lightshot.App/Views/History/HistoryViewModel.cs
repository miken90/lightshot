// Ported from LightshotKit/Sources/LightshotKit/HistoryStore.swift and App/Sources/HistoryModel.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Lightshot.Core;
using Lightshot.Platform.Windows.Settings;

namespace Lightshot.App.Views.History;

/// <summary>
/// View model for the capture history window.
/// </summary>
public class HistoryViewModel : INotifyPropertyChanged
{
    private readonly HistoryStore _store;
    private readonly IRecycleBin _recycleBin;
    private readonly IExplorerService _explorerService;
    private readonly Action<CapturedImage>? _onReopen;
    private readonly Action<CapturedImage>? _onCopy;
    private readonly Action<CaptureRecord>? _onReopenRecording;
    private readonly Action<string>? _onCopyFile;
    private readonly ISettingsStore? _settingsStore;

    private IReadOnlyList<CaptureRecord> _records = [];

    public IReadOnlyList<CaptureRecord> Records
    {
        get => _records;
        private set
        {
            _records = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public bool IsEmpty => _records.Count == 0;

    public int Retention
    {
        get => _store.Retention;
        set => SetRetention(value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public HistoryViewModel(
        HistoryStore store,
        IRecycleBin? recycleBin = null,
        IExplorerService? explorerService = null,
        Action<CapturedImage>? onReopen = null,
        Action<CapturedImage>? onCopy = null,
        Action<CaptureRecord>? onReopenRecording = null,
        Action<string>? onCopyFile = null,
        ISettingsStore? settingsStore = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _recycleBin = recycleBin ?? new DefaultRecycleBin();
        _explorerService = explorerService ?? new DefaultExplorerService();
        _onReopen = onReopen;
        _onCopy = onCopy;
        _onReopenRecording = onReopenRecording;
        _onCopyFile = onCopyFile;
        _settingsStore = settingsStore;

        if (_settingsStore != null)
        {
            string? raw = _settingsStore.GetSetting(SettingsKeys.HistoryRetention);
            if (raw != null && int.TryParse(raw, out int parsed))
            {
                _store.SetRetention(parsed);
            }
            else if (_settingsStore.HistoryRetention > 0)
            {
                _store.SetRetention(_settingsStore.HistoryRetention);
            }
        }

        Refresh();
    }

    public void Refresh()
    {
        Records = _store.All();
    }

    public void Reopen(CaptureRecord record)
    {
        if (record.Kind != CaptureKind.Screenshot)
        {
            _onReopenRecording?.Invoke(record);
            return;
        }

        var image = _store.CapturedImage(record);
        if (image.HasValue)
        {
            _onReopen?.Invoke(image.Value);
        }
    }

    public void Copy(CaptureRecord record)
    {
        if (record.Kind != CaptureKind.Screenshot)
        {
            _onCopyFile?.Invoke(record.FileUrl);
            return;
        }

        var image = _store.CapturedImage(record);
        if (image.HasValue)
        {
            if (_onCopy != null)
            {
                _onCopy(image.Value);
            }
            else
            {
                var bmp = QuickAccess.CardViewModel.CreateBitmapSource(image.Value);
                try
                {
                    Clipboard.SetImage(bmp);
                }
                catch
                {
                    // Clipboard access may fail if locked
                }
            }
        }
    }

    public void Reveal(CaptureRecord record)
    {
        _explorerService.RevealInExplorer(record.FileUrl);
    }

    public void Delete(CaptureRecord record)
    {
        _recycleBin.DeleteToRecycleBin(record.FileUrl);
        _recycleBin.DeleteToRecycleBin(record.ThumbnailUrl);
        _store.Remove(record);
        Refresh();
    }

    public void ClearAll()
    {
        foreach (var record in _records)
        {
            _recycleBin.DeleteToRecycleBin(record.FileUrl);
            _recycleBin.DeleteToRecycleBin(record.ThumbnailUrl);
        }
        _store.Clear();
        Refresh();
    }

    public void SetRetention(int value)
    {
        int capped = Math.Max(0, value);
        _store.SetRetention(capped);
        if (_settingsStore != null)
        {
            _settingsStore.HistoryRetention = capped;
            _settingsStore.SetSetting(SettingsKeys.HistoryRetention, capped.ToString());
        }
        Refresh();
        OnPropertyChanged(nameof(Retention));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
