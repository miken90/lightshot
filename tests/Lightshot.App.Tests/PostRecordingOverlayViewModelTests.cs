// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Linq;
using Lightshot.App.Views.QuickAccess;
using Lightshot.App.Views.Recording;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.App.Tests;

public class PostRecordingOverlayViewModelTests
{
    private class FakeClock : IClock
    {
        private DateTime _now = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
        private readonly List<ScheduledItem> _scheduled = [];

        public DateTime UtcNow => _now;

        public IDisposable Schedule(TimeSpan delay, Action callback)
        {
            var item = new ScheduledItem(_now + delay, callback);
            _scheduled.Add(item);
            return new Unsubscriber(() => _scheduled.Remove(item));
        }

        public void Advance(TimeSpan delta)
        {
            _now += delta;
            var due = _scheduled.Where(s => s.Due <= _now).ToList();
            foreach (var item in due)
            {
                _scheduled.Remove(item);
                item.Callback();
            }
        }

        private sealed class ScheduledItem
        {
            public DateTime Due { get; }
            public Action Callback { get; }

            public ScheduledItem(DateTime due, Action callback)
            {
                Due = due;
                Callback = callback;
            }
        }

        private sealed class Unsubscriber : IDisposable
        {
            private readonly Action _action;
            public Unsubscriber(Action action) => _action = action;
            public void Dispose() => _action();
        }
    }

    private class FakeMediaSink : IMediaSink
    {
        public List<string> CopiedFiles { get; } = [];
        public List<(string Source, string Dest)> SavedFiles { get; } = [];
        public List<string> TrashedFiles { get; } = [];
        public List<string> DeletedFiles { get; } = [];

        public void CopyFile(string path) => CopiedFiles.Add(path);
        public void Save(string sourcePath, string destinationPath) => SavedFiles.Add((sourcePath, destinationPath));
        public void Trash(string path) => TrashedFiles.Add(path);
        public void Delete(string path) => DeletedFiles.Add(path);
        public void Copy(string sourcePath, string destinationPath) => SavedFiles.Add((sourcePath, destinationPath));
    }

    [Fact]
    [Unit]
    public void AutoSavesAfter20Seconds()
    {
        var clock = new FakeClock();
        var sink = new FakeMediaSink();
        var recording = new PendingRecording(@"C:\Temp\take.mp4", RecordingOutputKind.Video, 15.0);

        string? dismissedName = null;
        var vm = new PostRecordingOverlayViewModel(
            recording,
            sink,
            clock,
            onDismiss: name => dismissedName = name);

        Assert.False(vm.IsSettled);
        Assert.Null(dismissedName);

        // Advance 10s: should NOT auto-save yet
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.False(vm.IsSettled);
        Assert.Null(dismissedName);

        // Advance another 10s (total 20s): should auto-save
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.True(vm.IsSettled);
        Assert.NotNull(dismissedName);
        Assert.Equal("take", dismissedName);
    }

    [Fact]
    [Unit]
    public void DeleteToRecycleBinViaFakeMediaSink()
    {
        var clock = new FakeClock();
        var sink = new FakeMediaSink();
        var recording = new PendingRecording(@"C:\Temp\take.mp4", RecordingOutputKind.Video, 8.0);

        var vm = new PostRecordingOverlayViewModel(recording, sink, clock);

        Assert.False(vm.IsSettled);
        Assert.False(vm.IsDeleted);

        vm.Trash();

        Assert.True(vm.IsSettled);
        Assert.True(vm.IsDeleted);
        Assert.Single(sink.TrashedFiles);
        Assert.Equal(@"C:\Temp\take.mp4", sink.TrashedFiles[0]);

        // Further timer advancement does not trigger duplicate dismissal
        clock.Advance(TimeSpan.FromSeconds(30));
    }

    [Fact]
    [Unit]
    public void CopyCallsMediaSinkCopyFile()
    {
        var clock = new FakeClock();
        var sink = new FakeMediaSink();
        var recording = new PendingRecording(@"C:\Temp\sample.mp4", RecordingOutputKind.Video, 5.0);

        var vm = new PostRecordingOverlayViewModel(recording, sink, clock);

        vm.Copy();

        Assert.True(vm.IsSettled);
        Assert.Single(sink.CopiedFiles);
        Assert.Equal(@"C:\Temp\sample.mp4", sink.CopiedFiles[0]);
    }

    [Fact]
    [Unit]
    public void RenameResetsAutoSaveTimer()
    {
        var clock = new FakeClock();
        var sink = new FakeMediaSink();
        var recording = new PendingRecording(@"C:\Temp\sample.mp4", RecordingOutputKind.Video, 5.0);

        var vm = new PostRecordingOverlayViewModel(recording, sink, clock);

        // Advance 15s
        clock.Advance(TimeSpan.FromSeconds(15));
        Assert.False(vm.IsSettled);

        // Rename (simulates user activity)
        vm.Name = "new_name";

        // Advance another 10s (25s total since start, but only 10s since rename) -> should not have fired yet
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.False(vm.IsSettled);

        // Advance another 10s (20s since rename) -> should now auto-save
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.True(vm.IsSettled);
        Assert.Equal("new_name", vm.Name);
    }

    [Fact]
    [Unit]
    public void CopyAndTrashRouteThroughCallbacksWhenSupplied()
    {
        var clock = new FakeClock();
        var sink = new FakeMediaSink();
        var recording = new PendingRecording(@"C:\Temp\sample.mp4", RecordingOutputKind.Video, 5.0, suggestedName: "custom_take");

        string? copiedName = null;
        bool deleted = false;

        var vm = new PostRecordingOverlayViewModel(
            recording,
            sink,
            clock,
            onCopy: name => copiedName = name,
            onDelete: () => deleted = true);

        vm.Copy();

        Assert.True(vm.IsSettled);
        Assert.Equal("custom_take", copiedName);
        Assert.Empty(sink.CopiedFiles);

        // Reset settled state with a new VM for Trash testing
        var vm2 = new PostRecordingOverlayViewModel(
            recording,
            sink,
            clock,
            onCopy: name => copiedName = name,
            onDelete: () => deleted = true);

        vm2.Trash();

        Assert.True(vm2.IsSettled);
        Assert.True(vm2.IsDeleted);
        Assert.True(deleted);
        Assert.Empty(sink.TrashedFiles);
    }
}
