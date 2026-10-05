// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lightshot.Core;
using Lightshot.Platform.Windows.Displays;

namespace Lightshot.Platform.Windows.Capture;

/// <summary>
/// Windows implementation of ICaptureService.
/// Coordinates display freeze, region crops, window enumeration, and window image capture.
/// </summary>
public class WindowsCaptureService : ICaptureService
{
    private readonly DisplayTopology _topology;
    private readonly ISettingsStore? _settings;
    private IReadOnlyList<WindowCandidate> _lastWindowCandidates = [];

    public bool RequestWaitsForAnswer => false;

    public WindowsCaptureService(DisplayTopology? topology = null, ISettingsStore? settings = null)
    {
        _topology = topology ?? new DisplayTopology();
        _settings = settings;
    }

    public Task<CaptureAuthorizationStatus> AuthorizationStatusAsync()
    {
        return Task.FromResult(CaptureAuthorizationStatus.Authorized);
    }

    public Task<CaptureAuthorizationStatus> RequestAuthorizationAsync()
    {
        return Task.FromResult(CaptureAuthorizationStatus.Authorized);
    }

    public Task<Result<CapturedImage, CaptureError>> CaptureFullscreenAsync(uint? displayId = null)
    {
        _topology.Refresh();
        var display = displayId.HasValue
            ? _topology.GetDisplay(displayId.Value)
            : _topology.PrimaryDisplay;

        if (display == null)
        {
            return Task.FromResult(Result<CapturedImage, CaptureError>.Failure(new CaptureError.NoDisplayAvailable()));
        }

        var captureResult = DdaDisplayCapture.CaptureDisplay(display);
        if (!captureResult.IsSuccess)
        {
            return Task.FromResult(captureResult);
        }

        var image = captureResult.Value;
        if (_settings?.IncludeCursor == true && image.Data.Length >= image.PixelWidth * image.PixelHeight * 4)
        {
            byte[] pixelCopy = image.Data.ToArray();
            CursorCompositor.CompositeCursor(pixelCopy, image.PixelWidth, image.PixelHeight, display.Bounds);
            image = new CapturedImage(image.PixelWidth, image.PixelHeight, pixelCopy);
        }

        return Task.FromResult(Result<CapturedImage, CaptureError>.Success(image));
    }

    public async Task<Result<CapturedImage, CaptureError>> CaptureRegionAsync(CaptureRegion region)
    {
        var freezeResult = await FreezeScreenAsync();
        if (!freezeResult.IsSuccess)
        {
            return Result<CapturedImage, CaptureError>.Failure(freezeResult.Error);
        }

        var frozen = freezeResult.Value;
        var image = frozen.ImageOf(region);
        if (image == null)
        {
            return Result<CapturedImage, CaptureError>.Failure(
                new CaptureError.SystemFailure("Specified capture region does not intersect any active display or window")
            );
        }

        return Result<CapturedImage, CaptureError>.Success(image.Value);
    }

    public Task<Result<FrozenScreen, CaptureError>> FreezeScreenAsync()
    {
        _topology.Refresh();
        var displays = _topology.Displays;
        if (displays.Count == 0)
        {
            return Task.FromResult(Result<FrozenScreen, CaptureError>.Failure(new CaptureError.NoDisplayAvailable()));
        }

        var frozenDisplays = new List<FrozenDisplay>(displays.Count);
        bool includeCursor = _settings?.IncludeCursor == true;

        foreach (var d in displays)
        {
            var captureResult = DdaDisplayCapture.CaptureDisplay(d);
            if (!captureResult.IsSuccess)
            {
                return Task.FromResult(Result<FrozenScreen, CaptureError>.Failure(captureResult.Error));
            }

            var image = captureResult.Value;
            if (includeCursor && image.Data.Length >= image.PixelWidth * image.PixelHeight * 4)
            {
                byte[] pixelCopy = image.Data.ToArray();
                CursorCompositor.CompositeCursor(pixelCopy, image.PixelWidth, image.PixelHeight, d.Bounds);
                image = new CapturedImage(image.PixelWidth, image.PixelHeight, pixelCopy);
            }

            frozenDisplays.Add(new FrozenDisplay(d.DisplayId, d.Bounds, image));
        }

        // Enumerate windows in desktop z-order
        _lastWindowCandidates = WindowEnumerator.EnumerateWindows();
        var frozenWindows = _lastWindowCandidates.Select(c => new FrozenWindow(c.Id, c.Bounds, null)).ToList();

        var frozenScreen = new FrozenScreen(frozenDisplays, frozenWindows);
        return Task.FromResult(Result<FrozenScreen, CaptureError>.Success(frozenScreen));
    }

    public Task<IReadOnlyDictionary<uint, CapturedImage>> FreezeWindowImagesAsync()
    {
        var map = new Dictionary<uint, CapturedImage>();
        var candidates = _lastWindowCandidates.Count > 0 ? _lastWindowCandidates : WindowEnumerator.EnumerateWindows();

        foreach (var c in candidates)
        {
            var captureResult = WgcWindowCapture.CaptureWindow(c.Handle, c.Bounds);
            if (captureResult.IsSuccess)
            {
                map[c.Id] = captureResult.Value;
            }
        }

        return Task.FromResult<IReadOnlyDictionary<uint, CapturedImage>>(map);
    }
}
