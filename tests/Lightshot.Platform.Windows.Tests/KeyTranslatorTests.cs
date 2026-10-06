using System;
using Lightshot.Core;
using Lightshot.Platform.Windows.Input;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class KeyTranslatorTests
{
    [Fact]
    [Unit]
    public void ShiftOneReadsShiftOne()
    {
        // 0x31 is VK_1 ('1' key)
        var press = KeyTranslator.Translate(0x31, KeyModifiers.Shift);

        Assert.NotNull(press);
        Assert.Equal("1", press.Value.Label);
        Assert.Equal(KeyModifiers.Shift, press.Value.Modifiers);
        // Core KeystrokeOverlay format: Shift modifier glyph (⇧) followed by unmodified key label ("1")
        Assert.Equal("⇧1", press.Value.Text);
    }

    [Fact]
    [Unit]
    public void ModifierKeysAloneReturnNull()
    {
        // Shift, Ctrl, Alt, Win alone should not produce KeyDown
        Assert.Null(KeyTranslator.Translate(0x10, KeyModifiers.Shift));   // VK_SHIFT
        Assert.Null(KeyTranslator.Translate(0x11, KeyModifiers.Control)); // VK_CONTROL
        Assert.Null(KeyTranslator.Translate(0x12, KeyModifiers.Option));  // VK_MENU (Alt)
        Assert.Null(KeyTranslator.Translate(0x5B, KeyModifiers.Command)); // VK_LWIN
    }

    [Fact]
    [Unit]
    public void NamedKeysProduceStandardSymbols()
    {
        Assert.Equal("↩", KeyTranslator.GetLabel(0x0D));      // VK_RETURN
        Assert.Equal("⇥", KeyTranslator.GetLabel(0x09));      // VK_TAB
        Assert.Equal("Space", KeyTranslator.GetLabel(0x20));   // VK_SPACE
        Assert.Equal("⌫", KeyTranslator.GetLabel(0x08));      // VK_BACK
        Assert.Equal("⌦", KeyTranslator.GetLabel(0x2E));      // VK_DELETE
        Assert.Equal("⎋", KeyTranslator.GetLabel(0x1B));      // VK_ESCAPE
        Assert.Equal("←", KeyTranslator.GetLabel(0x25));      // VK_LEFT
        Assert.Equal("↑", KeyTranslator.GetLabel(0x26));      // VK_UP
        Assert.Equal("→", KeyTranslator.GetLabel(0x27));      // VK_RIGHT
        Assert.Equal("↓", KeyTranslator.GetLabel(0x28));      // VK_DOWN
        Assert.Equal("F1", KeyTranslator.GetLabel(0x70));     // VK_F1
        Assert.Equal("F12", KeyTranslator.GetLabel(0x7B));    // VK_F12
    }

    [Fact]
    [Unit]
    public void ChordsWithLettersProduceCanonicalPillText()
    {
        // Ctrl+Z
        var ctrlZ = KeyTranslator.Translate('Z', KeyModifiers.Control);
        Assert.NotNull(ctrlZ);
        Assert.Equal("Z", ctrlZ.Value.Label);
        Assert.Equal("⌃Z", ctrlZ.Value.Text);

        // Win+E
        var winE = KeyTranslator.Translate('E', KeyModifiers.Command);
        Assert.NotNull(winE);
        Assert.Equal("E", winE.Value.Label);
        Assert.Equal("⌘E", winE.Value.Text);

        // Alt+Tab
        var altTab = KeyTranslator.Translate(0x09, KeyModifiers.Option);
        Assert.NotNull(altTab);
        Assert.Equal("⇥", altTab.Value.Label);
        Assert.Equal("⌥⇥", altTab.Value.Text);
    }
}
