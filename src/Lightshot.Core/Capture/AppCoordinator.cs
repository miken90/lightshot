// Ported from LightshotKit/Sources/LightshotKit/AppCoordinator.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using System.Threading.Tasks;

namespace Lightshot.Core;

/// <summary>
/// Composition root and use-case sequencer: coordinates capture flows, editor routing,
/// output saving/clipboard, and the screen recording lifecycle.
/// </summary>
public partial class AppCoordinator
{
    private readonly ICaptureService _captureService;
    private readonly IOverlayController _overlay;
    private readonly IImageSource _imageSource;
    private readonly IImageSink _imageSink;
    private readonly ISettingsStore _settings;
    private readonly HistoryStore? _history;
    private readonly IRecordingService? _recordingService;
    private readonly IMediaSink? _mediaSink;
    private readonly IGifEncoding? _gifEncoder;
    private readonly IMediaMetadataSource? _mediaMetadata;
    private readonly string _scratchDirectory;
    private readonly IImageRenderer? _renderer;
    private readonly IImageCodec? _codec;
    private readonly Func<double, Task> _sleep;
    private readonly Func<double> _clock;
    private readonly ICaptureUI _ui;

    private RecordingSession _recordingSession = new();
    private bool _isStartingRecording;
    private PendingRecording? _pendingRecording;
    private Task? _gifConversion;
    private bool _isArchiving;
    private bool _isFinishingTake;

    private abstract record LastCapture
    {
        public sealed record Fullscreen(uint? DisplayId) : LastCapture;
        public sealed record Area : LastCapture;
        public sealed record Window : LastCapture;
    }

    private LastCapture? _lastCapture;

    public RecordingSession RecordingSession => _recordingSession;
    public bool IsRecording => _recordingSession.IsActive;
    public bool HasWorkInProgress => IsRecording || _isStartingRecording || _isFinishingTake || (_gifConversion != null && !_gifConversion.IsCompleted) || _isArchiving;
    public double RecordingElapsed => _recordingSession.Elapsed(_clock());
    public PendingRecording? PendingRecording => _pendingRecording;
    public string RecordingScratchDirectory => Path.Combine(_scratchDirectory, "Lightshot Recordings").Replace('\\', '/');

    public AppCoordinator(
        ICaptureService captureService,
        IOverlayController overlay,
        IImageSource imageSource,
        IImageSink imageSink,
        ISettingsStore settings,
        HistoryStore? history = null,
        IRecordingService? recordingService = null,
        IMediaSink? mediaSink = null,
        IGifEncoding? gifEncoder = null,
        IMediaMetadataSource? mediaMetadata = null,
        string? scratchDirectory = null,
        IImageRenderer? renderer = null,
        IImageCodec? codec = null,
        Func<double, Task>? sleep = null,
        Func<double>? clock = null,
        ICaptureUI? ui = null)
    {
        _captureService = captureService;
        _overlay = overlay;
        _imageSource = imageSource;
        _imageSink = imageSink;
        _settings = settings;
        _history = history;
        _recordingService = recordingService;
        _mediaSink = mediaSink;
        _gifEncoder = gifEncoder;
        _mediaMetadata = mediaMetadata;
        _scratchDirectory = scratchDirectory ?? Path.GetTempPath();
        _renderer = renderer;
        _codec = codec;
        _sleep = sleep ?? (s => Task.Delay(TimeSpan.FromSeconds(Math.Max(0, s))));
        _clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0);
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
    }

    private RenderedImage Render(AnnotationDocument document)
    {
        if (_renderer != null)
        {
            return _renderer.Render(document);
        }

        return new RenderedImage(document.Image.PixelWidth, document.Image.PixelHeight, document.Image.Data.ToArray());
    }

    private byte[] Encode(RenderedImage image, ImageFormat format)
    {
        if (_codec != null)
        {
            return _codec.Encode(image, format);
        }

        return image.Data;
    }
}
