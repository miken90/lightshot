// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lightshot.Core.Tests.FakeServices;

public class FakeCaptureService : ICaptureService
{
    public Result<CapturedImage, CaptureError> Result { get; set; }
    public List<CaptureRegion> CapturedRegions { get; } = [];
    public List<uint?> CapturedDisplays { get; } = [];

    public CaptureAuthorizationStatus Status { get; set; } = CaptureAuthorizationStatus.Authorized;
    public CaptureAuthorizationStatus RequestResult { get; set; } = CaptureAuthorizationStatus.Authorized;
    public int AuthorizationStatusCount { get; private set; }
    public int RequestAuthorizationCount { get; private set; }

    public Result<FrozenScreen, CaptureError> FreezeResult { get; set; }
    public int FreezeCount { get; private set; }
    public Dictionary<uint, CapturedImage> WindowImages { get; set; } = new() { [4242] = CoreTestFixtures.SampleWindowImage() };
    public int WindowImagesRequestCount { get; private set; }

    public FakeCaptureService()
        : this(Result<CapturedImage, CaptureError>.Failure(new CaptureError.UserCancelled()))
    {
    }

    public FakeCaptureService(
        Result<CapturedImage, CaptureError> result,
        CaptureAuthorizationStatus status = CaptureAuthorizationStatus.Authorized,
        CaptureAuthorizationStatus requestResult = CaptureAuthorizationStatus.Authorized,
        Result<FrozenScreen, CaptureError>? freeze = null)
    {
        Result = result;
        Status = status;
        RequestResult = requestResult;
        FreezeResult = freeze ?? (result.IsSuccess
            ? CoreTestFixtures.SampleFrozenScreen()
            : Result<FrozenScreen, CaptureError>.Failure(result.Error));
    }

    public Task<CaptureAuthorizationStatus> AuthorizationStatusAsync()
    {
        AuthorizationStatusCount++;
        return Task.FromResult(Status);
    }

    public Task<CaptureAuthorizationStatus> RequestAuthorizationAsync()
    {
        RequestAuthorizationCount++;
        Status = RequestResult;
        return Task.FromResult(RequestResult);
    }

    public Task<Result<CapturedImage, CaptureError>> CaptureFullscreenAsync(uint? displayId = null)
    {
        CapturedDisplays.Add(displayId);
        return Task.FromResult(Result);
    }

    public Task<Result<CapturedImage, CaptureError>> CaptureRegionAsync(CaptureRegion region)
    {
        CapturedRegions.Add(region);
        return Task.FromResult(Result);
    }

    public Task<Result<FrozenScreen, CaptureError>> FreezeScreenAsync()
    {
        FreezeCount++;
        return Task.FromResult(FreezeResult);
    }

    public Task<IReadOnlyDictionary<uint, CapturedImage>> FreezeWindowImagesAsync()
    {
        WindowImagesRequestCount++;
        return Task.FromResult<IReadOnlyDictionary<uint, CapturedImage>>(WindowImages);
    }
}
