// Ported from LightshotKit/Tests/LightshotKitTests/RecordingSessionTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Core.Tests;

public class RecordingSessionTests
{
    private const string TestUrl = "/tmp/take.mp4";
    private static readonly CaptureRegion TestDisplay = new CaptureRegion.DisplayRegion(1);

    private static RecordingOptions CreateOptions(int countdown = 0) =>
        new(TestDisplay, new RecordingOutput.Video(VideoSettings.Standard), countdownSeconds: countdown);

    private static RecordingSession CreateRecordingSession()
    {
        var s = new RecordingSession();
        s.Start(CreateOptions(), TestUrl, 0);
        return s;
    }

    [Fact]
    [Unit]
    public void StartWithoutCountdownGoesStraightToRecording()
    {
        var s = CreateRecordingSession();
        Assert.Equal(RecordingSession.State.RecordingState, s.CurrentState);
        Assert.Equal(CreateOptions(), s.Options);
        Assert.Equal(TestUrl, s.OutputPath);
        Assert.True(s.IsActive);
    }

    [Fact]
    [Unit]
    public void StartWithCountdownWaitsForBeginRecording()
    {
        var s = new RecordingSession();
        s.Start(CreateOptions(countdown: 3), TestUrl, 0);
        Assert.Equal(RecordingSession.State.CountdownState, s.CurrentState);
        Assert.Equal(0.0, s.Elapsed(10));

        s.BeginRecording(10);
        Assert.Equal(RecordingSession.State.RecordingState, s.CurrentState);
        Assert.Equal(2.0, s.Elapsed(12));
    }

    [Fact]
    [Unit]
    public void StopThenFinishProducesTheFile()
    {
        var s = CreateRecordingSession();
        Assert.Equal(RecordingSession.StopOutcome.Stopping.Instance, s.Stop(5));
        Assert.Equal(RecordingSession.State.StoppingState, s.CurrentState);
        Assert.Equal(5.0, s.Elapsed(100));

        s.Finish(TestUrl);
        Assert.Equal(new RecordingSession.State.Finished(TestUrl), s.CurrentState);
        Assert.False(s.IsActive);
    }

    [Fact]
    [Unit]
    public void AFinishedOrFailedSessionCanStartAgain()
    {
        var s = CreateRecordingSession();
        _ = s.Stop(1);
        s.Finish(TestUrl);
        s.Start(CreateOptions(), TestUrl, 20);
        Assert.Equal(RecordingSession.State.RecordingState, s.CurrentState);
        Assert.Equal(1.0, s.Elapsed(21));

        s.Fail(RecordingError.DiskFull.Instance, 22);
        Assert.Equal(new RecordingSession.State.Failed(RecordingError.DiskFull.Instance), s.CurrentState);
        s.Start(CreateOptions(), TestUrl, 30);
        Assert.Equal(RecordingSession.State.RecordingState, s.CurrentState);
    }

    [Fact]
    [Unit]
    public void ElapsedExcludesPausedIntervalsAcrossSeveralCycles()
    {
        var s = CreateRecordingSession();
        s.Pause(4);
        Assert.Equal(4.0, s.Elapsed(50));

        s.Resume(50);
        Assert.Equal(7.0, s.Elapsed(53));

        s.Pause(53);
        s.Resume(60);
        s.Pause(62);
        Assert.Equal(9.0, s.Elapsed(999));
        Assert.Equal(RecordingSession.StopOutcome.Stopping.Instance, s.Stop(999));
        Assert.Equal(9.0, s.Elapsed(1000));
    }

    [Fact]
    [Unit]
    public void ElapsedNeverGoesNegativeWithAnEarlierClock()
    {
        var s = new RecordingSession();
        s.Start(CreateOptions(), TestUrl, 10);
        Assert.Equal(0.0, s.Elapsed(5));
    }

    [Fact]
    [Unit]
    public void RestartResetsTheClockAndReturnsThePartialFile()
    {
        var s = CreateRecordingSession();
        s.Pause(8);
        string partial = s.Restart(20);
        Assert.Equal(TestUrl, partial);
        Assert.Equal(RecordingSession.State.RecordingState, s.CurrentState);
        Assert.Equal(3.0, s.Elapsed(23));
        Assert.Equal(CreateOptions(), s.Options);
    }

    [Fact]
    [Unit]
    public void RestartReentersTheCountdownWhenOneIsConfigured()
    {
        var s = new RecordingSession();
        s.Start(CreateOptions(countdown: 3), TestUrl, 0);
        s.BeginRecording(3);
        s.Restart(10);
        Assert.Equal(RecordingSession.State.CountdownState, s.CurrentState);
        Assert.Equal(0.0, s.Elapsed(11));
    }

    [Fact]
    [Unit]
    public void DiscardEndsInIdleFromCountdownRecordingAndPaused()
    {
        Action<RecordingSession>[] preparers = [
            s => s.Start(CreateOptions(countdown: 3), TestUrl, 0),
            s => s.Start(CreateOptions(), TestUrl, 0),
            s => { s.Start(CreateOptions(), TestUrl, 0); s.Pause(1); }
        ];

        foreach (var prep in preparers)
        {
            var s = new RecordingSession();
            prep(s);
            Assert.Equal(TestUrl, s.Discard());
            Assert.Equal(RecordingSession.State.IdleState, s.CurrentState);
            Assert.Null(s.Options);
            Assert.Null(s.OutputPath);
            Assert.Equal(0.0, s.Elapsed(0));
        }
    }

    [Fact]
    [Unit]
    public void StopDuringCountdownBehavesLikeDiscard()
    {
        var s = new RecordingSession();
        s.Start(CreateOptions(countdown: 3), TestUrl, 0);
        Assert.Equal(new RecordingSession.StopOutcome.Discarded(TestUrl), s.Stop(1));
        Assert.Equal(RecordingSession.State.IdleState, s.CurrentState);
    }

    [Fact]
    [Unit]
    public void IllegalCommandsThrowAndLeaveTheSessionUnchanged()
    {
        var idle = new RecordingSession();
        Assert.Throws<RecordingSession.IllegalTransitionException>(() => idle.BeginRecording(0));
        Assert.Throws<RecordingSession.IllegalTransitionException>(() => idle.Pause(0));
        Assert.Throws<RecordingSession.IllegalTransitionException>(() => idle.Resume(0));
        Assert.Throws<RecordingSession.IllegalTransitionException>(() => idle.Stop(0));
        Assert.Throws<RecordingSession.IllegalTransitionException>(() => idle.Finish(TestUrl));
        Assert.Throws<RecordingSession.IllegalTransitionException>(() => idle.Fail(RecordingError.DiskFull.Instance, 0));
        Assert.Throws<RecordingSession.IllegalTransitionException>(() => idle.Restart(0));
        Assert.Throws<RecordingSession.IllegalTransitionException>(() => idle.Discard());
        Assert.Equal(RecordingSession.State.IdleState, idle.CurrentState);

        var recording = CreateRecordingSession();
        Assert.Throws<RecordingSession.IllegalTransitionException>(() => recording.Start(CreateOptions(), TestUrl, 1));
        Assert.Throws<RecordingSession.IllegalTransitionException>(() => recording.Resume(1));
        Assert.Throws<RecordingSession.IllegalTransitionException>(() => recording.Finish(TestUrl));
        Assert.Equal(RecordingSession.State.RecordingState, recording.CurrentState);

        var paused = CreateRecordingSession();
        paused.Pause(1);
        Assert.Throws<RecordingSession.IllegalTransitionException>(() => paused.Pause(2));
        Assert.Equal(RecordingSession.State.PausedState, paused.CurrentState);

        var counting = new RecordingSession();
        counting.Start(CreateOptions(countdown: 3), TestUrl, 0);
        Assert.Throws<RecordingSession.IllegalTransitionException>(() => counting.Restart(1));
        Assert.Throws<RecordingSession.IllegalTransitionException>(() => counting.Pause(1));
        Assert.Equal(RecordingSession.State.CountdownState, counting.CurrentState);

        var stopping = CreateRecordingSession();
        _ = stopping.Stop(1);
        Assert.Throws<RecordingSession.IllegalTransitionException>(() => stopping.Restart(2));
        Assert.Throws<RecordingSession.IllegalTransitionException>(() => stopping.Discard());
        Assert.Throws<RecordingSession.IllegalTransitionException>(() => stopping.Pause(2));
        Assert.Equal(RecordingSession.State.StoppingState, stopping.CurrentState);
    }

    [Fact]
    [Unit]
    public void EndedTakesAcceptNothingButStart()
    {
        var finished = CreateRecordingSession();
        _ = finished.Stop(1);
        finished.Finish(TestUrl);

        var failed = CreateRecordingSession();
        failed.Fail(RecordingError.DiskFull.Instance, 1);

        RecordingSession[] sessions = [finished, failed];
        foreach (var session in sessions)
        {
            var state = session.CurrentState;
            Assert.Throws<RecordingSession.IllegalTransitionException>(() => session.BeginRecording(2));
            Assert.Throws<RecordingSession.IllegalTransitionException>(() => session.Pause(2));
            Assert.Throws<RecordingSession.IllegalTransitionException>(() => session.Resume(2));
            Assert.Throws<RecordingSession.IllegalTransitionException>(() => session.Stop(2));
            Assert.Throws<RecordingSession.IllegalTransitionException>(() => session.Finish(TestUrl));
            Assert.Throws<RecordingSession.IllegalTransitionException>(() => session.Fail(RecordingError.DiskFull.Instance, 2));
            Assert.Throws<RecordingSession.IllegalTransitionException>(() => session.Restart(2));
            Assert.Throws<RecordingSession.IllegalTransitionException>(() => session.Discard());
            Assert.Equal(state, session.CurrentState);
        }
    }

    [Fact]
    [Unit]
    public void FailIsLegalFromEveryActiveStateAndKeepsElapsedTime()
    {
        var s = new RecordingSession();
        s.Start(CreateOptions(countdown: 2), TestUrl, 0);
        s.Fail(RecordingError.NoDisplayAvailable.Instance, 1);
        Assert.Equal(new RecordingSession.State.Failed(RecordingError.NoDisplayAvailable.Instance), s.CurrentState);

        var recording = CreateRecordingSession();
        recording.Fail(new RecordingError.SystemFailure("stream"), 30);
        Assert.Equal(30.0, recording.Elapsed(99));

        var paused = CreateRecordingSession();
        paused.Pause(4);
        paused.Fail(RecordingError.DiskFull.Instance, 50);
        Assert.Equal(4.0, paused.Elapsed(99));

        var stopping = CreateRecordingSession();
        _ = stopping.Stop(3);
        stopping.Fail(new RecordingError.SystemFailure("writer"), 5);
        Assert.Equal(new RecordingSession.State.Failed(new RecordingError.SystemFailure("writer")), stopping.CurrentState);
        Assert.Equal(3.0, stopping.Elapsed(9));
    }
}
