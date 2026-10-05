// Ported from LightshotKit/Tests/LightshotKitTests/PermissionOnboardingModelTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Core.Tests;

public class PermissionOnboardingModelTests
{
    private sealed class FakePermission : IPermissionAuthorizing
    {
        public CaptureAuthorizationStatus Status { get; set; }
        public CaptureAuthorizationStatus RequestResult { get; }
        public int StatusCount { get; private set; }
        public int RequestCount { get; private set; }
        public bool RequestWaitsForAnswer { get; }

        public FakePermission(CaptureAuthorizationStatus status, CaptureAuthorizationStatus? requestResult = null, bool waits = false)
        {
            Status = status;
            RequestResult = requestResult ?? status;
            RequestWaitsForAnswer = waits;
        }

        public Task<CaptureAuthorizationStatus> AuthorizationStatusAsync()
        {
            StatusCount++;
            return Task.FromResult(Status);
        }

        public Task<CaptureAuthorizationStatus> RequestAuthorizationAsync()
        {
            RequestCount++;
            Status = RequestResult;
            return Task.FromResult(RequestResult);
        }
    }

    private static PermissionOnboardingModel Onboarding(FakePermission source)
    {
        return new PermissionOnboardingModel([
            new PermissionOnboardingModel.Requirement(PermissionKind.ScreenRecording, "Screen Recording", "Capture your screen.", source)
        ]);
    }

    [Fact]
    [Unit]
    public async Task StartsNotDeterminedThenReflectsAStandingGrantOnRefresh()
    {
        var source = new FakePermission(CaptureAuthorizationStatus.Authorized);
        var model = Onboarding(source);

        Assert.Equal(CaptureAuthorizationStatus.NotDetermined, model.Requirements[0].Status);
        Assert.False(model.IsSatisfied);

        await model.RefreshAsync();

        Assert.Equal(1, source.StatusCount);
        Assert.Equal(CaptureAuthorizationStatus.Authorized, model.Requirements[0].Status);
        Assert.True(model.Requirements[0].IsGranted);
        Assert.True(model.IsSatisfied);
    }

    [Fact]
    [Unit]
    public async Task RefreshReflectsAStandingDenial()
    {
        var model = Onboarding(new FakePermission(CaptureAuthorizationStatus.Denied));

        await model.RefreshAsync();

        Assert.Equal(CaptureAuthorizationStatus.Denied, model.Requirements[0].Status);
        Assert.False(model.IsSatisfied);
    }

    [Fact]
    [Unit]
    public async Task EnableTriggersThePromptAndFoldsInTheGrant()
    {
        var source = new FakePermission(CaptureAuthorizationStatus.NotDetermined, CaptureAuthorizationStatus.Authorized);
        var model = Onboarding(source);

        await model.EnableAsync(PermissionKind.ScreenRecording);

        Assert.Equal(1, source.RequestCount);
        Assert.Equal(CaptureAuthorizationStatus.Authorized, model.Requirements[0].Status);
        Assert.True(model.IsSatisfied);
    }

    [Fact]
    [Unit]
    public async Task EnableOnAStandingDenialDoesNotProceed()
    {
        var source = new FakePermission(CaptureAuthorizationStatus.Denied, CaptureAuthorizationStatus.Denied);
        var model = Onboarding(source);

        await model.EnableAsync(PermissionKind.ScreenRecording);

        Assert.Equal(1, source.RequestCount);
        Assert.Equal(CaptureAuthorizationStatus.Denied, model.Requirements[0].Status);
        Assert.False(model.IsSatisfied);
    }

    [Fact]
    [Unit]
    public async Task EnablingADeniedRowStillAsksTheOSThenSendsTheUserToSettings()
    {
        var source = new FakePermission(CaptureAuthorizationStatus.Denied, CaptureAuthorizationStatus.Denied);
        var opened = new List<PermissionKind>();
        var model = new PermissionOnboardingModel(
            [new PermissionOnboardingModel.Requirement(PermissionKind.ScreenRecording, "Screen Recording", "Capture your screen.", source)],
            openSettings: opened.Add
        );
        await model.RefreshAsync();

        await model.EnableAsync(PermissionKind.ScreenRecording);

        Assert.Equal(1, source.RequestCount);
        Assert.Equal([PermissionKind.ScreenRecording], opened);
    }

    [Fact]
    [Unit]
    public async Task EnablingAFirstRunRowLeavesTheSystemPromptToGuideTheUser()
    {
        var source = new FakePermission(CaptureAuthorizationStatus.NotDetermined, CaptureAuthorizationStatus.Denied);
        var opened = new List<PermissionKind>();
        var model = new PermissionOnboardingModel(
            [new PermissionOnboardingModel.Requirement(PermissionKind.ScreenRecording, "Screen Recording", "Capture your screen.", source)],
            openSettings: opened.Add
        );
        await model.RefreshAsync();

        await model.EnableAsync(PermissionKind.ScreenRecording);

        Assert.Equal(1, source.RequestCount);
        Assert.Empty(opened);
    }

    [Fact]
    [Unit]
    public async Task EnablingADeniedRowThatTurnsOutGrantedSkipsSettings()
    {
        var source = new FakePermission(CaptureAuthorizationStatus.Denied, CaptureAuthorizationStatus.Authorized);
        var opened = new List<PermissionKind>();
        var model = new PermissionOnboardingModel(
            [new PermissionOnboardingModel.Requirement(PermissionKind.ScreenRecording, "Screen Recording", "Capture your screen.", source)],
            openSettings: opened.Add
        );
        await model.RefreshAsync();

        await model.EnableAsync(PermissionKind.ScreenRecording);

        Assert.Empty(opened);
        Assert.True(model.IsSatisfied);
    }

    [Fact]
    [Unit]
    public async Task RefreshPicksUpAGrantMadeOutsideTheApp()
    {
        var source = new FakePermission(CaptureAuthorizationStatus.Denied);
        var model = Onboarding(source);

        await model.RefreshAsync();
        Assert.False(model.IsSatisfied);

        source.Status = CaptureAuthorizationStatus.Authorized;
        await model.RefreshAsync();

        Assert.Equal(CaptureAuthorizationStatus.Authorized, model.Requirements[0].Status);
        Assert.True(model.IsSatisfied);
    }

    [Fact]
    [Unit]
    public async Task IsSatisfiedOnlyWhenEveryRequirementIsGranted()
    {
        var granted = new FakePermission(CaptureAuthorizationStatus.Authorized);
        var missing = new FakePermission(CaptureAuthorizationStatus.Denied);
        var model = new PermissionOnboardingModel([
            new PermissionOnboardingModel.Requirement(PermissionKind.ScreenRecording, "Screen Recording", "Capture your screen.", granted),
            new PermissionOnboardingModel.Requirement(PermissionKind.ScreenRecording, "Second", "Stand-in.", missing)
        ]);

        await model.RefreshAsync();
        Assert.False(model.IsSatisfied);

        missing.Status = CaptureAuthorizationStatus.Authorized;
        await model.RefreshAsync();

        bool allGranted = model.Requirements.All(r => r.IsGranted);
        Assert.True(allGranted);
        Assert.True(model.IsSatisfied);
    }
}
