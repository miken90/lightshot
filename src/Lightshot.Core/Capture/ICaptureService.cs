// Ported from LightshotKit/Sources/LightshotKit/CaptureService.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lightshot.Core;

/// <summary>
/// The OS capture seam: provides full display, region, and frozen screen captures.
/// </summary>
public interface ICaptureService : IPermissionAuthorizing
{
    Task<Result<CapturedImage, CaptureError>> CaptureFullscreenAsync(uint? displayId = null);
    Task<Result<CapturedImage, CaptureError>> CaptureRegionAsync(CaptureRegion region);
    Task<Result<FrozenScreen, CaptureError>> FreezeScreenAsync();
    Task<IReadOnlyDictionary<uint, CapturedImage>> FreezeWindowImagesAsync();
}
