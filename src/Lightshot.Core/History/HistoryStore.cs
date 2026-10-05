// Ported from LightshotKit/Sources/LightshotKit/HistoryStore.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lightshot.Core;

/// <summary>
/// The local capture history: records captures as they happen, lists them for the history view,
/// and enforces a retention cap that trims the oldest.
/// </summary>
public class HistoryStore
{
    public const int DefaultRetention = 50;
    public const int ThumbnailMaxPixelSize = 320;

    private static readonly DateTime SwiftReferenceDate = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _directory;
    private readonly string _indexFile;
    private readonly IThumbnailer? _thumbnailer;
    private readonly List<StoredEntry> _entries = [];

    public int Retention { get; private set; }

    public HistoryStore(string directory, int retention = DefaultRetention, IThumbnailer? thumbnailer = null)
    {
        _directory = directory;
        _indexFile = Path.Combine(directory, "index.json");
        _thumbnailer = thumbnailer;
        Retention = Math.Max(0, retention);

        LoadIndex();
    }

    /// <summary>
    /// Every recorded capture, newest first.
    /// </summary>
    public IReadOnlyList<CaptureRecord> All()
    {
        var list = new List<CaptureRecord>(_entries.Count);
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            list.Add(ToRecord(_entries[i]));
        }
        return list;
    }

    /// <summary>
    /// Rebuilds the CapturedImage for a record.
    /// Returns null if not a screenshot or if the file is missing/unreadable.
    /// </summary>
    public CapturedImage? CapturedImage(CaptureRecord record)
    {
        if (record.Kind != CaptureKind.Screenshot)
        {
            return null;
        }

        if (!File.Exists(record.FileUrl))
        {
            return null;
        }

        try
        {
            var bytes = File.ReadAllBytes(record.FileUrl);
            return new CapturedImage(record.PixelWidth, record.PixelHeight, bytes);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the record for an ID if present.
    /// </summary>
    public CaptureRecord? Record(Guid id)
    {
        var entry = _entries.FirstOrDefault(e => e.Id == id);
        return entry == null ? null : ToRecord(entry);
    }

    /// <summary>
    /// Records a screenshot as it happens.
    /// </summary>
    public CaptureRecord Add(CapturedImage image, CaptureSource source, DateTime? date = null)
    {
        EnsureDirectory();

        var id = Guid.NewGuid();
        var imageFileName = $"{id}.png";
        var thumbFileName = $"{id}-thumb.png";

        var imageFilePath = Path.Combine(_directory, imageFileName);
        var thumbFilePath = Path.Combine(_directory, thumbFileName);

        File.WriteAllBytes(imageFilePath, image.Data.ToArray());

        var thumbBytes = _thumbnailer?.CreateThumbnail(image.Data.ToArray(), ThumbnailMaxPixelSize)
                         ?? image.Data.ToArray();
        File.WriteAllBytes(thumbFilePath, thumbBytes);

        var entry = new StoredEntry
        {
            Id = id,
            Timestamp = date ?? DateTime.UtcNow,
            Source = source,
            Kind = CaptureKind.Screenshot,
            PixelWidth = image.PixelWidth,
            PixelHeight = image.PixelHeight,
            Duration = null,
            ImageFile = imageFileName,
            ThumbnailFile = thumbFileName
        };

        return Append(entry);
    }

    /// <summary>
    /// Records a finished video or media take by moving it into the store.
    /// </summary>
    public CaptureRecord Add(
        string mediaPath,
        CaptureKind kind,
        int pixelWidth,
        int pixelHeight,
        double? duration,
        ReadOnlyMemory<byte> thumbnail,
        CaptureSource source,
        DateTime? date = null)
    {
        if (Retention <= 0)
        {
            throw new HistoryException("History is off", HistoryError.HistoryOff);
        }

        EnsureDirectory();

        var id = Guid.NewGuid();
        var ext = Path.GetExtension(mediaPath);
        var mediaFileName = string.IsNullOrEmpty(ext) ? id.ToString() : $"{id}{ext}";
        var thumbFileName = $"{id}-thumb.png";

        var mediaTarget = Path.Combine(_directory, mediaFileName);
        var thumbTarget = Path.Combine(_directory, thumbFileName);

        File.WriteAllBytes(thumbTarget, thumbnail.ToArray());

        try
        {
            File.Move(mediaPath, mediaTarget);
        }
        catch
        {
            try { File.Delete(thumbTarget); } catch { }
            throw;
        }

        var entry = new StoredEntry
        {
            Id = id,
            Timestamp = date ?? DateTime.UtcNow,
            Source = source,
            Kind = kind,
            PixelWidth = pixelWidth,
            PixelHeight = pixelHeight,
            Duration = duration,
            ImageFile = mediaFileName,
            ThumbnailFile = thumbFileName
        };

        try
        {
            return Append(entry);
        }
        catch
        {
            _entries.RemoveAll(e => e.Id == id);
            try { File.Move(mediaTarget, mediaPath); } catch { }
            try { File.Delete(thumbTarget); } catch { }
            throw;
        }
    }

    /// <summary>
    /// Records a GIF through GIF metadata/thumbnail decoding if available.
    /// Throws UnreadableMedia if the file is not a valid GIF or decoder is unavailable.
    /// </summary>
    public CaptureRecord AddGif(string gifPath, CaptureSource source, DateTime? date = null)
    {
        throw new HistoryException($"Unreadable media: {gifPath}", HistoryError.UnreadableMedia);
    }

    /// <summary>
    /// Removes a single history item and deletes its owned files.
    /// </summary>
    public void Remove(CaptureRecord record)
    {
        var index = _entries.FindIndex(e => e.Id == record.Id);
        if (index < 0) return;

        var entry = _entries[index];
        _entries.RemoveAt(index);
        DeleteFiles(entry);
        Persist();
    }

    /// <summary>
    /// Empties the history and deletes all owned files.
    /// </summary>
    public void Clear()
    {
        foreach (var entry in _entries)
        {
            DeleteFiles(entry);
        }
        _entries.Clear();
        Persist();
    }

    /// <summary>
    /// Sets retention cap and trims immediately if needed.
    /// </summary>
    public void SetRetention(int value)
    {
        Retention = Math.Max(0, value);
        Trim();
        Persist();
    }

    private CaptureRecord Append(StoredEntry entry)
    {
        _entries.Add(entry);
        _entries.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        Trim();
        Persist();
        return ToRecord(entry);
    }

    private void Trim()
    {
        if (_entries.Count <= Retention) return;

        int overflow = _entries.Count - Retention;
        for (int i = 0; i < overflow; i++)
        {
            DeleteFiles(_entries[i]);
        }
        _entries.RemoveRange(0, overflow);
    }

    private void DeleteFiles(StoredEntry entry)
    {
        var imagePath = Path.Combine(_directory, entry.ImageFile);
        var thumbPath = Path.Combine(_directory, entry.ThumbnailFile);
        try { if (File.Exists(imagePath)) File.Delete(imagePath); } catch { }
        try { if (File.Exists(thumbPath)) File.Delete(thumbPath); } catch { }
    }

    private CaptureRecord ToRecord(StoredEntry entry)
    {
        return new CaptureRecord(
            entry.Id,
            entry.Timestamp,
            entry.Source,
            entry.Kind,
            entry.PixelWidth,
            entry.PixelHeight,
            entry.Duration,
            Path.Combine(_directory, entry.ImageFile).Replace('\\', '/'),
            Path.Combine(_directory, entry.ThumbnailFile).Replace('\\', '/')
        );
    }

    private void EnsureDirectory()
    {
        if (!Directory.Exists(_directory))
        {
            Directory.CreateDirectory(_directory);
        }
    }

    private void Persist()
    {
        EnsureDirectory();
        var index = new StoredIndex { Records = _entries };
        var json = JsonSerializer.Serialize(index, JsonOptions);
        File.WriteAllText(_indexFile, json);
    }

    private void LoadIndex()
    {
        if (!File.Exists(_indexFile)) return;

        try
        {
            var json = File.ReadAllText(_indexFile);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("records", out var recordsElement)
                && recordsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in recordsElement.EnumerateArray())
                {
                    var id = Guid.Parse(item.GetProperty("id").GetString()!);

                    DateTime timestamp;
                    var tsProp = item.GetProperty("timestamp");
                    if (tsProp.ValueKind == JsonValueKind.Number)
                    {
                        double seconds = tsProp.GetDouble();
                        timestamp = SwiftReferenceDate.AddSeconds(seconds);
                    }
                    else
                    {
                        timestamp = DateTime.Parse(tsProp.GetString()!);
                    }

                    var sourceStr = item.GetProperty("source").GetString()!;
                    var source = Enum.Parse<CaptureSource>(sourceStr, ignoreCase: true);

                    var kind = CaptureKind.Screenshot;
                    if (item.TryGetProperty("kind", out var kindProp) && kindProp.ValueKind == JsonValueKind.String)
                    {
                        kind = Enum.Parse<CaptureKind>(kindProp.GetString()!, ignoreCase: true);
                    }

                    int pixelWidth = item.GetProperty("pixelWidth").GetInt32();
                    int pixelHeight = item.GetProperty("pixelHeight").GetInt32();

                    double? duration = null;
                    if (item.TryGetProperty("duration", out var durProp) && durProp.ValueKind == JsonValueKind.Number)
                    {
                        duration = durProp.GetDouble();
                    }

                    var imageFile = item.GetProperty("imageFile").GetString()!;
                    var thumbFile = item.GetProperty("thumbnailFile").GetString()!;

                    _entries.Add(new StoredEntry
                    {
                        Id = id,
                        Timestamp = timestamp,
                        Source = source,
                        Kind = kind,
                        PixelWidth = pixelWidth,
                        PixelHeight = pixelHeight,
                        Duration = duration,
                        ImageFile = imageFile,
                        ThumbnailFile = thumbFile
                    });
                }
            }
        }
        catch
        {
            // Ignore load errors and start clean
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private sealed class StoredIndex
    {
        public List<StoredEntry> Records { get; set; } = [];
    }

    private sealed class StoredEntry
    {
        public Guid Id { get; set; }
        public DateTime Timestamp { get; set; }
        public CaptureSource Source { get; set; }
        public CaptureKind Kind { get; set; }
        public int PixelWidth { get; set; }
        public int PixelHeight { get; set; }
        public double? Duration { get; set; }
        public string ImageFile { get; set; } = "";
        public string ThumbnailFile { get; set; } = "";
    }
}
