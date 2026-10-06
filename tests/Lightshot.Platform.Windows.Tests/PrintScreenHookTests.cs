using System;
using System.Collections.Generic;
using Lightshot.Core;
using Lightshot.Platform.Windows.Hotkeys;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class PrintScreenHookTests
{
    private static (PrintScreenHook Hook, List<CaptureAction> Fired) BoundHook()
    {
        var fired = new List<CaptureAction>();
        var hook = new PrintScreenHook(fired.Add);
        hook.SetChords(new Dictionary<HotkeyModifiers, CaptureAction>
        {
            [HotkeyModifiers.None] = CaptureAction.Area,
            [HotkeyModifiers.Control] = CaptureAction.Fullscreen
        });
        return (hook, fired);
    }

    [Fact]
    [Unit]
    public void BoundPrintScreenFiresOncePerPressAndSwallowsItsKeyUp()
    {
        var (hook, fired) = BoundHook();
        using var _ = hook;

        Assert.True(hook.IsInstalled);
        Assert.True(hook.HandleKey(PrintScreenHook.VK_SNAPSHOT, isDown: true, HotkeyModifiers.None));
        Assert.True(hook.HandleKey(PrintScreenHook.VK_SNAPSHOT, isDown: true, HotkeyModifiers.None));
        Assert.True(hook.HandleKey(PrintScreenHook.VK_SNAPSHOT, isDown: false, HotkeyModifiers.None));
        Assert.True(hook.HandleKey(PrintScreenHook.VK_SNAPSHOT, isDown: true, HotkeyModifiers.Control));
        Assert.True(hook.HandleKey(PrintScreenHook.VK_SNAPSHOT, isDown: false, HotkeyModifiers.Control));

        Assert.Equal(new[] { CaptureAction.Area, CaptureAction.Fullscreen }, fired);
    }

    [Fact]
    [Unit]
    public void UnboundChordsAndOtherKeysPassThrough()
    {
        var (hook, fired) = BoundHook();
        using var _ = hook;

        Assert.False(hook.HandleKey(PrintScreenHook.VK_SNAPSHOT, isDown: true, HotkeyModifiers.Shift));
        Assert.False(hook.HandleKey(PrintScreenHook.VK_SNAPSHOT, isDown: false, HotkeyModifiers.Shift));
        Assert.False(hook.HandleKey(0x41, isDown: true, HotkeyModifiers.None));
        Assert.Empty(fired);
    }

    [Fact]
    [Unit]
    public void UnbindingAChordWhileItIsHeldLetsTheNextPressThrough()
    {
        var (hook, fired) = BoundHook();
        using var _ = hook;

        Assert.True(hook.HandleKey(PrintScreenHook.VK_SNAPSHOT, isDown: true, HotkeyModifiers.None));
        // The key-up is lost (secure desktop, lock screen) and the user clears the binding meanwhile.
        hook.SetChords(new Dictionary<HotkeyModifiers, CaptureAction>());

        Assert.False(hook.HandleKey(PrintScreenHook.VK_SNAPSHOT, isDown: true, HotkeyModifiers.None));
        Assert.Equal(new[] { CaptureAction.Area }, fired);
    }

    [Fact]
    [Unit]
    public void AThrowingHandlerLeavesTheChordReadyForTheNextPress()
    {
        int calls = 0;
        using var hook = new PrintScreenHook(_ =>
        {
            calls++;
            if (calls == 1) throw new InvalidOperationException("shell thread is shutting down");
        });
        hook.SetChords(new Dictionary<HotkeyModifiers, CaptureAction> { [HotkeyModifiers.None] = CaptureAction.Area });

        Assert.Throws<InvalidOperationException>(() =>
            hook.HandleKey(PrintScreenHook.VK_SNAPSHOT, isDown: true, HotkeyModifiers.None));
        Assert.True(hook.HandleKey(PrintScreenHook.VK_SNAPSHOT, isDown: true, HotkeyModifiers.None));
        Assert.Equal(2, calls);
    }
}
