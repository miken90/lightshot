using Lightshot.Core;
using Lightshot.Platform.Windows.Hotkeys;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class VirtualKeyNamesTests
{
    [Fact]
    [Unit]
    public void MapsVirtualKeysToNamesAndBack()
    {
        Assert.Equal("PrintScreen", VirtualKeyNames.GetKeyName(0x2C));
        Assert.Equal("Escape", VirtualKeyNames.GetKeyName(0x1B));
        Assert.Equal("Return", VirtualKeyNames.GetKeyName(0x0D));
        Assert.Equal("F9", VirtualKeyNames.GetKeyName(0x78));
        Assert.Equal("A", VirtualKeyNames.GetKeyName(0x41));

        Assert.Equal((ushort)0x2C, VirtualKeyNames.GetVirtualKey("PrintScreen"));
        Assert.Equal((ushort)0x2C, VirtualKeyNames.GetVirtualKey("PrtSc"));
        Assert.Equal((ushort)0x1B, VirtualKeyNames.GetVirtualKey("Escape"));
        Assert.Equal((ushort)0x1B, VirtualKeyNames.GetVirtualKey("Esc"));
        Assert.Equal((ushort)0x0D, VirtualKeyNames.GetVirtualKey("Return"));
        Assert.Equal((ushort)0x0D, VirtualKeyNames.GetVirtualKey("Enter"));
        Assert.Equal((ushort)0x78, VirtualKeyNames.GetVirtualKey("F9"));
    }

    [Fact]
    [Unit]
    public void MapsModifiersToWin32AndBack()
    {
        var mods = HotkeyModifiers.Control | HotkeyModifiers.Shift;
        uint win32 = VirtualKeyNames.ToWin32Modifiers(mods, noRepeat: true);

        Assert.True((win32 & VirtualKeyNames.MOD_CONTROL) != 0);
        Assert.True((win32 & VirtualKeyNames.MOD_SHIFT) != 0);
        Assert.True((win32 & VirtualKeyNames.MOD_NOREPEAT) != 0);

        var roundTrip = VirtualKeyNames.FromWin32Modifiers(win32);
        Assert.Equal(mods, roundTrip);
    }

    [Fact]
    [Unit]
    public void FormatsChordDisplayString()
    {
        var binding1 = new HotkeyBinding(0x2C, HotkeyModifiers.None, "PrintScreen");
        Assert.Equal("PrintScreen", VirtualKeyNames.FormatChord(binding1));

        var binding2 = new HotkeyBinding(0x2C, HotkeyModifiers.Control, "PrintScreen");
        Assert.Equal("Ctrl+PrintScreen", VirtualKeyNames.FormatChord(binding2));

        var binding3 = new HotkeyBinding(0x78, HotkeyModifiers.Control | HotkeyModifiers.Shift, "F9");
        Assert.Equal("Ctrl+Shift+F9", VirtualKeyNames.FormatChord(binding3));
    }
}
