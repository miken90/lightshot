// Ported from LightshotKit/Tests/LightshotKitTests/KeystrokeOverlayTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Linq;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Core.Tests;

public class KeystrokeOverlayTests
{
    private static readonly KeyPress CmdZ = new("Z", KeyModifiers.Command);

    [Fact]
    [Unit]
    public void APressShowsModifierGlyphsThenTheKeyAndFadesAfterItsHold()
    {
        var m = new KeystrokeOverlayModel(KeystrokeOverlaySettings.Standard);
        Assert.Empty(m.Items(0));

        m.KeyDown(new KeyPress("S", KeyModifiers.Shift | KeyModifiers.Command | KeyModifiers.Control | KeyModifiers.Option | KeyModifiers.Function), 1);
        var fresh = m.Items(1.0);
        Assert.Single(fresh);
        Assert.Equal("fn⌃⌥⇧⌘S", fresh[0].Text);
        Assert.Equal(1.0, fresh[0].Opacity);
        Assert.Equal(KeystrokeOverlayModel.BumpScale, fresh[0].Scale);

        Assert.Equal(1.0, m.Items(1.5)[0].Scale);
        Assert.Equal(1.0, m.Items(2.4)[0].Opacity);

        double fading = m.Items(2.5 + 0.175)[0].Opacity;
        Assert.True(Math.Abs(fading - 0.5) < 0.001);

        Assert.Empty(m.Items(2.9));
        m.Prune(2.9);
        Assert.Empty(m.Items(2.9));
    }

    [Fact]
    [Unit]
    public void CommandOnlyModeHidesPlainTypingButShowsChords()
    {
        var m = new KeystrokeOverlayModel(new KeystrokeOverlaySettings(KeystrokeDisplayMode.CommandOnly));
        m.KeyDown(new KeyPress("A"), 0);
        m.KeyDown(new KeyPress("A", KeyModifiers.Shift), 0);
        Assert.Empty(m.Items(0));

        m.KeyDown(CmdZ, 0);
        m.KeyDown(new KeyPress("→", KeyModifiers.Option), 0);
        Assert.Equal(["⌘Z ⌥→"], m.Items(0).Select(i => i.Text).ToList());

        var all = new KeystrokeOverlayModel(new KeystrokeOverlaySettings(KeystrokeDisplayMode.AllKeys));
        all.KeyDown(new KeyPress("A"), 0);
        Assert.Equal(["A"], all.Items(0).Select(i => i.Text).ToList());
    }

    [Fact]
    [Unit]
    public void RepeatedPressesCountUpAndReBumpInsteadOfStacking()
    {
        var m = new KeystrokeOverlayModel(KeystrokeOverlaySettings.Standard);
        m.KeyDown(CmdZ, 0);
        m.KeyDown(CmdZ, 0.5);
        m.KeyDown(CmdZ, 0.9);
        var items = m.Items(0.9);
        Assert.Equal(["⌘Z ×3"], items.Select(i => i.Text).ToList());
        Assert.Equal(KeystrokeOverlayModel.BumpScale, items[0].Scale);

        m.KeyDown(new KeyPress("A"), 1.2);
        m.KeyDown(CmdZ, 1.4);
        Assert.Equal(["⌘Z ×3 A ⌘Z"], m.Items(1.4).Select(i => i.Text).ToList());
    }

    [Fact]
    [Unit]
    public void AHeldKeyAutoRepeatsWithoutCountingOrBumping()
    {
        var m = new KeystrokeOverlayModel(KeystrokeOverlaySettings.Standard);
        var left = new KeyPress("←");
        m.KeyDown(left, 0);
        for (int i = 1; i <= 40; i++)
        {
            m.KeyDown(new KeyPress("←", isRepeat: true), 0.5 + i * 0.05);
        }
        var items = m.Items(2.5);
        Assert.Equal(["←"], items.Select(i => i.Text).ToList());
        Assert.True(items[0].Opacity == 1.0 && items[0].Scale == 1.0);
        Assert.Empty(m.Items(4.5));

        m.KeyDown(left, 5);
        Assert.Equal(["←"], m.Items(5).Select(i => i.Text).ToList());
    }

    [Fact]
    [Unit]
    public void ContinuousTypingJoinsOnePillUntilTheTypingStops()
    {
        var m = new KeystrokeOverlayModel(KeystrokeOverlaySettings.Standard);
        m.KeyDown(new KeyPress("H", KeyModifiers.Shift), 0);
        string[] hello = ["E", "L", "L", "O"];
        for (int i = 0; i < hello.Length; i++)
        {
            m.KeyDown(new KeyPress(hello[i]), 0.2 + i * 0.2);
        }
        Assert.Equal(["HELLO"], m.Items(1.0).Select(i => i.Text).ToList());

        double fading = 2.4;
        m.KeyDown(new KeyPress("!"), fading);
        Assert.Equal([new KeystrokeItem("HELLO!", 1.0, KeystrokeOverlayModel.BumpScale)], m.Items(fading));

        double gone = fading + KeystrokeOverlayModel.HoldDuration + KeystrokeOverlayModel.FadeDuration;
        Assert.Empty(m.Items(gone));
        m.KeyDown(new KeyPress("A"), gone + 0.1);
        Assert.Equal(["A"], m.Items(gone + 0.1).Select(i => i.Text).ToList());
    }

    [Fact]
    [Unit]
    public void ChordsAndNamedKeysAreTheirOwnTokensAndSpaceShowsInsideText()
    {
        var m = new KeystrokeOverlayModel(KeystrokeOverlaySettings.Standard);
        m.KeyDown(new KeyPress("F", KeyModifiers.Shift | KeyModifiers.Command), 0);
        m.KeyDown(new KeyPress("S", KeyModifiers.Command), 0.3);
        m.KeyDown(new KeyPress("H"), 0.5);
        m.KeyDown(new KeyPress("I"), 0.6);
        m.KeyDown(new KeyPress("Space"), 0.7);
        m.KeyDown(new KeyPress("Y"), 0.8);
        m.KeyDown(new KeyPress("↩"), 0.9);
        m.KeyDown(new KeyPress("O"), 1.0);
        Assert.Equal(["⇧⌘F ⌘S HI␣Y ↩ O"], m.Items(1.0).Select(i => i.Text).ToList());
    }

    [Fact]
    [Unit]
    public void ALongBurstKeepsItsNewestCharacters()
    {
        var m = new KeystrokeOverlayModel(KeystrokeOverlaySettings.Standard);
        string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZABCDEFGHIJKLMN";
        for (int i = 0; i < letters.Length; i++)
        {
            m.KeyDown(new KeyPress(letters[i].ToString()), i * 0.05);
        }
        string text = m.Items(2)[0].Text;
        Assert.Equal(KeystrokeOverlayModel.MaxCharacters, text.Length);
        Assert.StartsWith("…", text);
        Assert.EndsWith("KLMN", text);
    }

    [Fact]
    [Unit]
    public void HeldModifiersShowWhileNothingFreshIsOnScreen()
    {
        var m = new KeystrokeOverlayModel(KeystrokeOverlaySettings.Standard);
        m.ModifiersChanged(KeyModifiers.Command);
        Assert.Equal([new KeystrokeItem("⌘", 1.0, 1.0)], m.Items(0));

        m.KeyDown(CmdZ, 0.2);
        Assert.Equal(["⌘Z"], m.Items(0.2).Select(i => i.Text).ToList());
        Assert.Equal(["⌘Z", "⌘"], m.Items(1.8).Select(i => i.Text).ToList());

        m.ModifiersChanged(KeyModifiers.None);
        Assert.Equal(["⌘Z"], m.Items(1.8).Select(i => i.Text).ToList());
    }

    [Fact]
    [Unit]
    public void SecureInputShowsNothingClearsPendingAndResumesOnlyWhenOff()
    {
        var m = new KeystrokeOverlayModel(KeystrokeOverlaySettings.Standard);
        m.KeyDown(CmdZ, 0);
        m.ModifiersChanged(KeyModifiers.Command);
        m.SetSecureInput(true);
        Assert.Empty(m.Items(0));
        Assert.Equal(KeyModifiers.None, m.HeldModifiers);

        m.KeyDown(new KeyPress("P"), 0.1);
        m.KeyDown(new KeyPress("P", KeyModifiers.Command), 0.1);
        m.ModifiersChanged(KeyModifiers.Shift);
        Assert.Empty(m.Items(0.1));

        m.SetSecureInput(false);
        Assert.Empty(m.Items(0.2));

        m.KeyDown(new KeyPress("Q"), 0.3);
        Assert.Equal(["Q"], m.Items(0.3).Select(i => i.Text).ToList());
    }

    [Fact]
    [Unit]
    public void EventsRouteThroughHandle()
    {
        var m = new KeystrokeOverlayModel(KeystrokeOverlaySettings.Standard);
        m.Handle(new KeyEvent.ModifiersChanged(KeyModifiers.Option), 0);
        m.Handle(new KeyEvent.KeyDown(new KeyPress("X", KeyModifiers.Option)), 0);
        Assert.Equal(["⌥X"], m.Items(0).Select(i => i.Text).ToList());

        m.Handle(new KeyEvent.SecureInput(true), 1);
        Assert.True(m.Items(1).Count == 0 && m.SecureInput);
    }

    [Fact]
    [Unit]
    public void KeyLabelsNameSpecialKeysAndUppercaseTheRest()
    {
        Assert.Equal("↩", KeyLabel.Label(36, "\r"));
        Assert.Equal("⎋", KeyLabel.Label(53, "\u001B"));
        Assert.Equal("←", KeyLabel.Label(123, "\uF702"));
        Assert.Equal("Space", KeyLabel.Label(49, " "));
        Assert.Equal("F1", KeyLabel.Label(122, "\uF704"));
        Assert.Equal("A", KeyLabel.Label(0, "a"));
        Assert.Equal("-", KeyLabel.Label(27, "-"));
        Assert.Null(KeyLabel.Label(0, "\uF710"));
        Assert.Null(KeyLabel.Label(0, null));
    }

    [Fact]
    [Unit]
    public void ThePositionAnchorsThePillGroupInsideTheFrame()
    {
        var frame = new Size(1000, 600);
        var pill = new Size(200, 50);
        Assert.Equal(new Rect(20, 20, 200, 50), KeystrokeOverlayPosition.TopLeft.Rect(pill, frame, 20));
        Assert.Equal(new Rect(400, 530, 200, 50), KeystrokeOverlayPosition.BottomCenter.Rect(pill, frame, 20));
        Assert.Equal(new Rect(780, 530, 200, 50), KeystrokeOverlayPosition.BottomRight.Rect(pill, frame, 20));
        Assert.True(KeystrokeOverlayPosition.TopCenter.IsTop() && !KeystrokeOverlayPosition.BottomLeft.IsTop());
    }
}
