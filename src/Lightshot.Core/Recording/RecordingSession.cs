// Ported from LightshotKit/Sources/LightshotKit/RecordingSession.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;

namespace Lightshot.Core;

/// <summary>
/// The pure state machine behind one recording session.
/// </summary>
public class RecordingSession
{
    public abstract record State
    {
        public sealed record Idle : State;
        public sealed record Countdown : State;
        public sealed record Recording : State;
        public sealed record Paused : State;
        public sealed record Stopping : State;
        public sealed record Finished(string OutputPath) : State;
        public sealed record Failed(RecordingError Error) : State;

        public static readonly Idle IdleState = new();
        public static readonly Countdown CountdownState = new();
        public static readonly Recording RecordingState = new();
        public static readonly Paused PausedState = new();
        public static readonly Stopping StoppingState = new();
    }

    public enum Command
    {
        Start,
        BeginRecording,
        Pause,
        Resume,
        Stop,
        Finish,
        Fail,
        Restart,
        Discard
    }

    public sealed class IllegalTransitionException : InvalidOperationException
    {
        public State CurrentState { get; }
        public Command AttemptedCommand { get; }

        public IllegalTransitionException(State state, Command command)
            : base($"Illegal transition: cannot execute '{command}' from state '{state.GetType().Name}'.")
        {
            CurrentState = state;
            AttemptedCommand = command;
        }
    }

    public abstract record StopOutcome
    {
        public sealed record Stopping : StopOutcome
        {
            public static readonly Stopping Instance = new();
        }

        public sealed record Discarded(string PartialFile) : StopOutcome;
    }

    public State CurrentState { get; private set; } = State.IdleState;
    public RecordingOptions? Options { get; private set; }
    public string? OutputPath { get; private set; }

    private double _recordedBeforeSegment;
    private double? _segmentStart;

    public RecordingSession() { }

    public bool IsActive => CurrentState switch
    {
        State.Countdown or State.Recording or State.Paused or State.Stopping => true,
        _ => false
    };

    public double Elapsed(double now)
    {
        if (CurrentState is State.Recording && _segmentStart.HasValue)
        {
            return _recordedBeforeSegment + Math.Max(0.0, now - _segmentStart.Value);
        }
        return _recordedBeforeSegment;
    }

    public void Start(RecordingOptions options, string url, double now)
    {
        switch (CurrentState)
        {
            case State.Idle or State.Finished or State.Failed:
                break;
            default:
                throw new IllegalTransitionException(CurrentState, Command.Start);
        }

        Options = options;
        OutputPath = url;
        ResetClock();
        EnterRecordingOrCountdown(options, now);
    }

    public void BeginRecording(double now)
    {
        if (CurrentState is not State.Countdown)
        {
            throw new IllegalTransitionException(CurrentState, Command.BeginRecording);
        }

        CurrentState = State.RecordingState;
        _segmentStart = now;
    }

    public void Pause(double now)
    {
        if (CurrentState is not State.Recording)
        {
            throw new IllegalTransitionException(CurrentState, Command.Pause);
        }

        BankSegment(now);
        CurrentState = State.PausedState;
    }

    public void Resume(double now)
    {
        if (CurrentState is not State.Paused)
        {
            throw new IllegalTransitionException(CurrentState, Command.Resume);
        }

        CurrentState = State.RecordingState;
        _segmentStart = now;
    }

    public StopOutcome Stop(double now)
    {
        switch (CurrentState)
        {
            case State.Recording:
                BankSegment(now);
                CurrentState = State.StoppingState;
                return StopOutcome.Stopping.Instance;

            case State.Paused:
                CurrentState = State.StoppingState;
                return StopOutcome.Stopping.Instance;

            case State.Countdown:
                string url = Discard();
                return new StopOutcome.Discarded(url);

            default:
                throw new IllegalTransitionException(CurrentState, Command.Stop);
        }
    }

    public void Finish(string url)
    {
        if (CurrentState is not State.Stopping)
        {
            throw new IllegalTransitionException(CurrentState, Command.Finish);
        }

        CurrentState = new State.Finished(url);
        OutputPath = url;
    }

    public void Fail(RecordingError error, double now)
    {
        if (!IsActive)
        {
            throw new IllegalTransitionException(CurrentState, Command.Fail);
        }

        BankSegment(now);
        CurrentState = new State.Failed(error);
    }

    public string Restart(double now)
    {
        if ((CurrentState is not State.Recording && CurrentState is not State.Paused) || Options is null || OutputPath is null)
        {
            throw new IllegalTransitionException(CurrentState, Command.Restart);
        }

        string url = OutputPath;
        ResetClock();
        EnterRecordingOrCountdown(Options, now);
        return url;
    }

    public string Discard()
    {
        switch (CurrentState)
        {
            case State.Countdown or State.Recording or State.Paused:
                break;
            default:
                throw new IllegalTransitionException(CurrentState, Command.Discard);
        }

        if (OutputPath is null)
        {
            throw new IllegalTransitionException(CurrentState, Command.Discard);
        }

        string url = OutputPath;
        CurrentState = State.IdleState;
        Options = null;
        OutputPath = null;
        ResetClock();
        return url;
    }

    private void EnterRecordingOrCountdown(RecordingOptions options, double now)
    {
        if (options.HasCountdown)
        {
            CurrentState = State.CountdownState;
        }
        else
        {
            CurrentState = State.RecordingState;
            _segmentStart = now;
        }
    }

    private void ResetClock()
    {
        _recordedBeforeSegment = 0.0;
        _segmentStart = null;
    }

    private void BankSegment(double now)
    {
        if (_segmentStart.HasValue)
        {
            _recordedBeforeSegment += Math.Max(0.0, now - _segmentStart.Value);
        }
        _segmentStart = null;
    }
}
