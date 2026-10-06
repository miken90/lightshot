// Ported from LightshotKit/Sources/LightshotKit/AppCoordinator.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lightshot.Core;

public partial class AppCoordinator
{
    // 1 while a screenshot flow runs. A second trigger (hotkey, tray, second launch) during the
    // overlay would stack a second overlay and yield a second image, so it is ignored.
    private int _captureInProgress;

    private async Task RunExclusiveCaptureAsync(Func<Task> flow)
    {
        if (Interlocked.Exchange(ref _captureInProgress, 1) == 1)
        {
            return;
        }

        try
        {
            await flow();
        }
        finally
        {
            Volatile.Write(ref _captureInProgress, 0);
        }
    }

    /// <summary>
    /// Fullscreen capture flow: checks onboarding, applies delay, captures display, routes to editor/QA.
    /// </summary>
    public Task CaptureFullscreenAsync(uint? displayId = null) =>
        RunExclusiveCaptureAsync(() => RunFullscreenCaptureAsync(displayId));

    private async Task RunFullscreenCaptureAsync(uint? displayId)
    {
        _lastCapture = new LastCapture.Fullscreen(displayId);
        if (!await GuideFirstRunAuthorizationIfNeededAsync(_captureService))
        {
            return;
        }

        await ApplyCaptureDelayAsync();

        var outcome = await _captureService.CaptureFullscreenAsync(displayId);
        if (outcome.IsSuccess)
        {
            Record(outcome.Value, CaptureSource.Fullscreen);
            PresentCapture(outcome.Value);
        }
        else
        {
            RouteCaptureFailure(outcome.Error);
        }
    }

    /// <summary>
    /// Area capture flow: checks onboarding, freezes screen (or delays if timed), opens overlay, routes crop.
    /// </summary>
    public Task CaptureAreaAsync() => RunExclusiveCaptureAsync(RunAreaCaptureAsync);

    private async Task RunAreaCaptureAsync()
    {
        _lastCapture = new LastCapture.Area();
        if (!await GuideFirstRunAuthorizationIfNeededAsync(_captureService))
        {
            return;
        }

        bool adjustable = _settings.AdjustAreaBeforeCapture;
        if (_settings.CaptureDelay > 0)
        {
            await CaptureLiveAreaAsync(adjustable);
            return;
        }

        var pair = await SelectFrozenAreaAsync(adjustable);
        if (pair == null)
        {
            return;
        }

        Record(pair.Value.Image, CaptureSource.Area);
        PresentCapture(pair.Value.Image);
    }

    private async Task CaptureLiveAreaAsync(bool adjustable)
    {
        var region = await _overlay.SelectRegionAsync(null, adjustable);
        if (region == null)
        {
            return;
        }

        await ApplyCaptureDelayAsync();

        var outcome = await _captureService.CaptureRegionAsync(region);
        if (outcome.IsSuccess)
        {
            Record(outcome.Value, CaptureSource.Area);
            PresentCapture(outcome.Value);
        }
        else
        {
            RouteCaptureFailure(outcome.Error);
        }
    }

    private async Task<(CaptureRegion Region, CapturedImage Image)?> SelectFrozenAreaAsync(bool adjustable)
    {
        var frozen = await FreezeScreenAsync();
        if (frozen == null)
        {
            return null;
        }

        var region = await _overlay.SelectRegionAsync(frozen, adjustable);
        if (region == null)
        {
            return null;
        }

        var image = frozen.ImageOf(region);
        if (image == null)
        {
            _ui.PresentCaptureFailure(new CaptureError.SystemFailure("The selection is outside the screen."));
            return null;
        }

        return (region, image.Value);
    }

    private async Task<FrozenScreen?> FreezeScreenAsync()
    {
        var outcome = await _captureService.FreezeScreenAsync();
        if (outcome.IsSuccess)
        {
            return outcome.Value;
        }

        RouteCaptureFailure(outcome.Error);
        return null;
    }

    private void RouteCaptureFailure(CaptureError error)
    {
        switch (error)
        {
            case CaptureError.PermissionDenied:
                _ui.PresentPermissionDenied(PermissionKind.ScreenRecording);
                break;
            case CaptureError.UserCancelled:
                break;
            default:
                _ui.PresentCaptureFailure(error);
                break;
        }
    }

    private void PresentCapture(CapturedImage image)
    {
        if (_settings.OpenInEditor)
        {
            _ui.OpenEditor(image);
        }
        else
        {
            _ui.PresentQuickAccess(image);
        }
    }

    private async Task<bool> GuideFirstRunAuthorizationIfNeededAsync(IPermissionAuthorizing service)
    {
        if (await service.AuthorizationStatusAsync() != CaptureAuthorizationStatus.NotDetermined)
        {
            return true;
        }

        return await service.RequestAuthorizationAsync() == CaptureAuthorizationStatus.Authorized;
    }

    /// <summary>
    /// Window capture flow: checks onboarding, freezes screen with windows, opens picker, routes capture.
    /// </summary>
    public Task CaptureWindowAsync() => RunExclusiveCaptureAsync(RunWindowCaptureAsync);

    private async Task RunWindowCaptureAsync()
    {
        _lastCapture = new LastCapture.Window();
        if (!await GuideFirstRunAuthorizationIfNeededAsync(_captureService))
        {
            return;
        }

        bool isTimed = _settings.CaptureDelay > 0;
        var pair = await SelectFrozenWindowAsync(!isTimed);
        if (pair == null)
        {
            return;
        }

        if (pair.Value.Image != null)
        {
            Record(pair.Value.Image.Value, CaptureSource.Window);
            PresentCapture(pair.Value.Image.Value);
            return;
        }

        await ApplyCaptureDelayAsync();

        var outcome = await _captureService.CaptureRegionAsync(pair.Value.Region);
        if (outcome.IsSuccess)
        {
            Record(outcome.Value, CaptureSource.Window);
            PresentCapture(outcome.Value);
        }
        else
        {
            RouteCaptureFailure(outcome.Error);
        }
    }

    private async Task<(CaptureRegion Region, CapturedImage? Image)?> SelectFrozenWindowAsync(bool withImages)
    {
        Task<IReadOnlyDictionary<uint, CapturedImage>>? windowImagesTask = withImages
            ? _captureService.FreezeWindowImagesAsync()
            : null;

        var frozen = await FreezeScreenAsync();
        if (frozen == null)
        {
            return null;
        }

        var region = await _overlay.SelectWindowAsync(frozen);
        if (region == null)
        {
            return null;
        }

        if (!withImages)
        {
            return (region, null);
        }

        if (windowImagesTask != null)
        {
            var images = await windowImagesTask;
            frozen = frozen.WithWindowImages(images);
        }

        return (region, frozen.ImageOf(region));
    }

    /// <summary>
    /// Repeats whichever capture mode (fullscreen, area, window) the user ran most recently.
    /// </summary>
    public async Task RepeatLastCaptureAsync()
    {
        switch (_lastCapture)
        {
            case LastCapture.Fullscreen f:
                await CaptureFullscreenAsync(f.DisplayId);
                break;
            case LastCapture.Area:
                await CaptureAreaAsync();
                break;
            case LastCapture.Window:
                await CaptureWindowAsync();
                break;
            case null:
                break;
        }
    }

    private async Task ApplyCaptureDelayAsync()
    {
        double seconds = _settings.CaptureDelay;
        if (seconds > 0)
        {
            await _sleep(seconds);
        }
    }

    /// <summary>
    /// Open-existing-file flow: asks user to pick file, decodes and opens in editor.
    /// </summary>
    public void OpenFile()
    {
        var outcome = _imageSource.OpenDocument();
        if (outcome.IsSuccess)
        {
            _ui.OpenEditor(outcome.Value);
        }
        else if (outcome.Error is ImageLoadError.UserCancelled)
        {
            // Silent no-op
        }
        else
        {
            _ui.PresentImageLoadFailure(outcome.Error);
        }
    }

    private void Record(CapturedImage image, CaptureSource source)
    {
        try
        {
            _history?.Add(image, source);
        }
        catch
        {
            // Best effort: history write failures do not block the user
        }
    }

    /// <summary>
    /// Places the flattened rendered image on the clipboard.
    /// Writes no file to disk.
    /// </summary>
    public void CopyToClipboard(AnnotationDocument document)
    {
        _imageSink.CopyToClipboard(Render(document));
    }

    /// <summary>
    /// Saves the rendered document to an explicit destination path and format.
    /// </summary>
    public void Save(AnnotationDocument document, string destinationPath, ImageFormat format)
    {
        _imageSink.Write(Render(document), destinationPath, format);
    }

    /// <summary>
    /// Saves the rendered document using configured defaults.
    /// </summary>
    public string Save(AnnotationDocument document, DateTime? date = null)
    {
        var path = _settings.DefaultDestination(date);
        Save(document, path, _settings.DefaultFormat);
        return path;
    }

    /// <summary>
    /// Builds the drag-out payload for the editor.
    /// </summary>
    public ImageDragItem DragItem(AnnotationDocument document, DateTime? date = null)
    {
        var format = _settings.DefaultFormat;
        var data = Encode(Render(document), format);
        var name = new FilenameFormatter(_settings.FilenamePattern).Filename(date ?? DateTime.Now);
        return new ImageDragItem(data, format, name);
    }
}
