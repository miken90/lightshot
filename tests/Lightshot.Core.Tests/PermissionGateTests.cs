// Ported from LightshotKit/Tests/LightshotKitTests/PermissionGateTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Threading.Tasks;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Core.Tests;

public class PermissionGateTests
{
    private sealed class FakePermission : IPermissionAuthorizing
    {
        public CaptureAuthorizationStatus Status { get; set; }
        public CaptureAuthorizationStatus AfterRequest { get; }
        public bool RequestWaitsForAnswer { get; }
        public int Requests { get; private set; }

        public FakePermission(CaptureAuthorizationStatus status, CaptureAuthorizationStatus afterRequest = CaptureAuthorizationStatus.Authorized, bool waits = false)
        {
            Status = status;
            AfterRequest = afterRequest;
            RequestWaitsForAnswer = waits;
        }

        public Task<CaptureAuthorizationStatus> AuthorizationStatusAsync() => Task.FromResult(Status);

        public Task<CaptureAuthorizationStatus> RequestAuthorizationAsync()
        {
            Requests++;
            Status = AfterRequest;
            return Task.FromResult(AfterRequest);
        }
    }

    [Fact]
    [Unit]
    public async Task AStandingGrantPassesWithoutPrompting()
    {
        var mic = new FakePermission(CaptureAuthorizationStatus.Authorized);
        Assert.Equal(PermissionOutcome.Granted, await PermissionGate.EnsureAsync(mic));
        Assert.Equal(0, mic.Requests);
    }

    [Fact]
    [Unit]
    public async Task AFirstAskThatWaitsReportsTheUsersDecision()
    {
        var granted = new FakePermission(CaptureAuthorizationStatus.NotDetermined, CaptureAuthorizationStatus.Authorized, waits: true);
        Assert.Equal(PermissionOutcome.Granted, await PermissionGate.EnsureAsync(granted));
        Assert.Equal(1, granted.Requests);

        var declined = new FakePermission(CaptureAuthorizationStatus.NotDetermined, CaptureAuthorizationStatus.Denied, waits: true);
        Assert.Equal(PermissionOutcome.Denied, await PermissionGate.EnsureAsync(declined));
    }

    [Fact]
    [Unit]
    public async Task AFirstAskThatReturnsBeforeTheAnswerIsPromptingNotDenied()
    {
        var screen = new FakePermission(CaptureAuthorizationStatus.NotDetermined, CaptureAuthorizationStatus.Denied);
        Assert.Equal(PermissionOutcome.Prompting, await PermissionGate.EnsureAsync(screen));
    }

    [Fact]
    [Unit]
    public async Task ABelievedDenialIsStillAskedSoAForgottenAppGetsListedAgain()
    {
        var reset = new FakePermission(CaptureAuthorizationStatus.Denied, CaptureAuthorizationStatus.Authorized);
        Assert.Equal(PermissionOutcome.Granted, await PermissionGate.EnsureAsync(reset));
        Assert.Equal(1, reset.Requests);

        var still = new FakePermission(CaptureAuthorizationStatus.Denied, CaptureAuthorizationStatus.Denied);
        Assert.Equal(PermissionOutcome.Denied, await PermissionGate.EnsureAsync(still));
        Assert.Equal(1, still.Requests);
    }

    [Fact]
    [Unit]
    public void EachToggleNamesTheGrantItNeeds()
    {
        Assert.Equal(PermissionKind.Microphone, RecordingToggle.Microphone.RequiredPermission());
        Assert.Equal(PermissionKind.Camera, RecordingToggle.Camera.RequiredPermission());
        Assert.Equal(PermissionKind.InputMonitoring, RecordingToggle.ShowKeystrokes.RequiredPermission());
        Assert.Null(RecordingToggle.ComputerAudio.RequiredPermission());
        Assert.Null(RecordingToggle.HighlightClicks.RequiredPermission());

        PermissionKind[] expected = [
            PermissionKind.ScreenRecording, PermissionKind.Microphone,
            PermissionKind.Camera, PermissionKind.InputMonitoring,
            PermissionKind.SpeechRecognition
        ];
        Assert.Equal(expected, Enum.GetValues<PermissionKind>());
    }
}
