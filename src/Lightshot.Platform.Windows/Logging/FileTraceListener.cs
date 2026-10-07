// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Lightshot.Platform.Windows.Logging;

public sealed class FileTraceListener : TraceListener
{
    private readonly string _logDirectory;
    private readonly long _maxFileSizeBytes;
    private readonly int _maxPartsPerDay;
    private readonly Func<DateTime> _clock;
    private readonly object _lock = new();

    private DateTime _currentDate;
    private StreamWriter? _writer;
    private string? _currentFilePath;
    private int _partIndex;
    private bool _dayTruncated;

    public FileTraceListener(
        string logDirectory,
        long maxFileSizeBytes = 5 * 1024 * 1024,
        Func<DateTime>? clock = null,
        int maxPartsPerDay = 3)
        : base("LightshotFileTraceListener")
    {
        _logDirectory = logDirectory;
        _maxFileSizeBytes = maxFileSizeBytes;
        _maxPartsPerDay = maxPartsPerDay;
        _clock = clock ?? (() => DateTime.UtcNow);
        _currentDate = _clock().Date;
    }

    public string? CurrentFilePath
    {
        get
        {
            lock (_lock)
            {
                return _currentFilePath;
            }
        }
    }

    public override void Write(string? message)
    {
        if (message == null) return;
        lock (_lock)
        {
            EnsureWriter();
            _writer?.Write(message);
        }
    }

    public override void WriteLine(string? message)
    {
        lock (_lock)
        {
            EnsureWriter();
            var ts = _clock().ToString("yyyy-MM-dd HH:mm:ss.fff");
            _writer?.WriteLine($"[{ts}] {message}");
        }
    }

    public override void Flush()
    {
        lock (_lock)
        {
            _writer?.Flush();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_lock)
            {
                _writer?.Flush();
                _writer?.Dispose();
                _writer = null;
            }
        }
        base.Dispose(disposing);
    }

    private void EnsureWriter()
    {
        var today = _clock().Date;
        if (_writer == null || today != _currentDate)
        {
            if (today != _currentDate)
            {
                _currentDate = today;
                _partIndex = 0;
                _dayTruncated = false;
            }
            else if (_dayTruncated)
            {
                return;
            }

            OpenFile();
            return;
        }

        if (_dayTruncated)
        {
            return;
        }

        // Check size cap
        if (_currentFilePath != null && File.Exists(_currentFilePath))
        {
            _writer.Flush();
            var info = new FileInfo(_currentFilePath);
            if (info.Length >= _maxFileSizeBytes)
            {
                if (_partIndex < _maxPartsPerDay)
                {
                    _partIndex++;
                    OpenFile();
                }
                else
                {
                    var ts = _clock().ToString("yyyy-MM-dd HH:mm:ss.fff");
                    _writer.WriteLine($"[{ts}] [LOG TRUNCATED: Daily file limit reached]");
                    _writer.Flush();
                    _writer.Dispose();
                    _writer = null;
                    _dayTruncated = true;
                }
            }
        }
    }

    private void OpenFile()
    {
        _writer?.Flush();
        _writer?.Dispose();
        _writer = null;

        Directory.CreateDirectory(_logDirectory);
        var baseName = $"lightshot-{_currentDate:yyyy-MM-dd}";

        while (_partIndex < _maxPartsPerDay)
        {
            var fileName = _partIndex == 0 ? $"{baseName}.log" : $"{baseName}.{_partIndex}.log";
            var filePath = Path.Combine(_logDirectory, fileName);
            if (File.Exists(filePath) && new FileInfo(filePath).Length >= _maxFileSizeBytes)
            {
                _partIndex++;
                continue;
            }
            break;
        }

        if (_partIndex >= _maxPartsPerDay)
        {
            var fileName = $"{baseName}.{_maxPartsPerDay}.log";
            var filePath = Path.Combine(_logDirectory, fileName);
            if (File.Exists(filePath) && new FileInfo(filePath).Length >= _maxFileSizeBytes)
            {
                _dayTruncated = true;
                return;
            }
        }

        var activeName = _partIndex == 0 ? $"{baseName}.log" : $"{baseName}.{_partIndex}.log";
        _currentFilePath = Path.Combine(_logDirectory, activeName);

        var stream = new FileStream(_currentFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = false };
    }
}
