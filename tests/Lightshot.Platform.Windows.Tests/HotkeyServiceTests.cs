using System;
using System.Collections.Generic;
using System.Threading;
using Lightshot.Core;
using Lightshot.Platform.Windows.Hotkeys;
using Lightshot.Platform.Windows.Interop;
using Lightshot.Platform.Windows.Shell;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class HotkeyServiceTests
{
    private const byte VK_CONTROL = 0x11;
    private const byte VK_SHIFT = 0x10;
    private const byte VK_F9 = 0x78;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [Fact]
    [Desktop]
    public void FiresOnChordAndReportsConflict()
    {
        using var shell = new ShellThread("HotkeyTestShell");
        using var service = new WindowsHotkeyService(shell);

        CaptureAction? firedAction = null;
        using var firedEvent = new ManualResetEventSlim(false);

        // 1. Register test hotkey Ctrl+Shift+F9 for Area capture
        var testBinding = new HotkeyBinding(VK_F9, HotkeyModifiers.Control | HotkeyModifiers.Shift, "F9");
        var bindings = new HotkeyBindings(new Dictionary<CaptureAction, HotkeyBinding>
        {
            [CaptureAction.Area] = testBinding
        });

        var failed = service.Register(bindings, action =>
        {
            firedAction = action;
            firedEvent.Set();
        });

        Assert.Empty(failed);

        // 2. Synthesize hotkey chord input
        Win32Window.keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
        Win32Window.keybd_event(VK_SHIFT, 0, 0, UIntPtr.Zero);
        Win32Window.keybd_event(VK_F9, 0, 0, UIntPtr.Zero);

        Win32Window.keybd_event(VK_F9, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        Win32Window.keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        Win32Window.keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

        bool signaled = firedEvent.Wait(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.True(signaled, "Expected hotkey event to fire on Ctrl+Shift+F9 chord");
        Assert.Equal(CaptureAction.Area, firedAction);

        // 3. Test conflict reporting: assign two actions to the exact same chord
        var conflictBindings = new HotkeyBindings(new Dictionary<CaptureAction, HotkeyBinding>
        {
            [CaptureAction.Area] = testBinding,
            [CaptureAction.Fullscreen] = testBinding // Conflict!
        });

        var conflictFailed = service.Register(conflictBindings, _ => { });
        Assert.Single(conflictFailed);
        Assert.Equal(CaptureAction.Fullscreen, conflictFailed[0]);

        service.UnregisterAll();
    }
}
