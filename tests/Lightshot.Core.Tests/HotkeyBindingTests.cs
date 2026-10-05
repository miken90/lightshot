// Ported from LightshotKit/Tests/LightshotKitTests/HotkeyBindingTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Core.Tests;

public class HotkeyBindingTests
{
    [Fact]
    [Unit]
    public void ChordRendersModifiersInCanonicalOrderThenKey()
    {
        var binding = new HotkeyBinding(21, HotkeyModifiers.Command | HotkeyModifiers.Shift, "4");
        Assert.Equal("⇧⌘4", binding.DisplayString);
    }

    [Fact]
    [Unit]
    public void AllModifiersRenderControlOptionShiftCommand()
    {
        var binding = new HotkeyBinding(0, HotkeyModifiers.Control | HotkeyModifiers.Option | HotkeyModifiers.Shift | HotkeyModifiers.Command, "a");
        Assert.Equal("⌃⌥⇧⌘A", binding.DisplayString);
    }

    [Fact]
    [Unit]
    public void DefaultsBindAreaAndFullscreenWithoutConflicts()
    {
        var defaults = HotkeyBindings.Defaults;
        Assert.NotNull(defaults[CaptureAction.Area]);
        Assert.NotNull(defaults[CaptureAction.Fullscreen]);
        Assert.Null(defaults[CaptureAction.Window]);
        Assert.Null(defaults[CaptureAction.RepeatLast]);
        Assert.Null(defaults[CaptureAction.RecordScreen]);
        Assert.Null(defaults[CaptureAction.PauseResumeRecording]);
        Assert.Null(defaults[CaptureAction.RestartRecording]);
        Assert.Null(defaults[CaptureAction.CaptureText]);
        Assert.Empty(defaults.Conflicts);
    }

    [Fact]
    [Unit]
    public void DefaultsArePrintScreenVariants()
    {
        var defaults = HotkeyBindings.Defaults;
        var area = defaults[CaptureAction.Area];
        var fullscreen = defaults[CaptureAction.Fullscreen];
        Assert.NotNull(area);
        Assert.NotNull(fullscreen);
        Assert.Equal(0x2C, area.Value.KeyCode);
        Assert.Equal(HotkeyModifiers.None, area.Value.Modifiers);
        Assert.Equal(0x2C, fullscreen.Value.KeyCode);
        Assert.Equal(HotkeyModifiers.Control, fullscreen.Value.Modifiers);
    }

    [Fact]
    [Unit]
    public void RecordingActionsTakePartInConflictDetection()
    {
        var bindings = HotkeyBindings.Defaults;
        var areaChord = bindings[CaptureAction.Area]!.Value;
        bindings[CaptureAction.RecordScreen] = areaChord;
        Assert.Single(bindings.Conflicts);
        Assert.Equal(new HotkeyConflict(areaChord, [CaptureAction.Area, CaptureAction.RecordScreen]), bindings.Conflicts[0]);
        Assert.Equal(CaptureAction.Area, bindings.ConflictingAction(bindings[CaptureAction.RecordScreen]!.Value, excluding: CaptureAction.RecordScreen));
    }

    [Fact]
    [Unit]
    public void OcrTextIsListedAfterRecordScreenAndTakesPartInConflictDetection()
    {
        Assert.Equal("OCR Text", CaptureAction.CaptureText.Title());
        Assert.True(CaptureAction.RecordScreen < CaptureAction.CaptureText);
        Assert.True(CaptureAction.CaptureText < CaptureAction.PauseResumeRecording);
        var bindings = HotkeyBindings.Defaults;
        var fullscreenChord = bindings[CaptureAction.Fullscreen]!.Value;
        bindings[CaptureAction.CaptureText] = fullscreenChord;
        Assert.Equal(CaptureAction.Fullscreen, bindings.ConflictingAction(bindings[CaptureAction.CaptureText]!.Value, excluding: CaptureAction.CaptureText));
    }

    [Fact]
    [Unit]
    public void DistinctChordsProduceNoConflict()
    {
        var bindings = new HotkeyBindings();
        bindings[CaptureAction.Area] = new HotkeyBinding(21, HotkeyModifiers.Command, "4");
        bindings[CaptureAction.Fullscreen] = new HotkeyBinding(20, HotkeyModifiers.Command, "3");
        Assert.Empty(bindings.Conflicts);
    }

    [Fact]
    [Unit]
    public void TwoActionsSharingAChordAreSurfacedAsAConflict()
    {
        var shared = new HotkeyBinding(21, HotkeyModifiers.Command | HotkeyModifiers.Shift, "4");
        var bindings = new HotkeyBindings();
        bindings[CaptureAction.Area] = shared;
        bindings[CaptureAction.Fullscreen] = shared;

        Assert.Single(bindings.Conflicts);
        Assert.Equal(shared, bindings.Conflicts[0].Binding);
        Assert.Equal([CaptureAction.Area, CaptureAction.Fullscreen], bindings.Conflicts[0].Actions);
    }

    [Fact]
    [Unit]
    public void ConflictComparesKeyAndModifiersNotTheCosmeticLabel()
    {
        var bindings = new HotkeyBindings();
        bindings[CaptureAction.Area] = new HotkeyBinding(49, HotkeyModifiers.Command, "Space");
        bindings[CaptureAction.Fullscreen] = new HotkeyBinding(49, HotkeyModifiers.Command, " ");
        Assert.Single(bindings.Conflicts);
    }

    [Fact]
    [Unit]
    public void DifferingModifiersDoNotConflict()
    {
        var bindings = new HotkeyBindings();
        bindings[CaptureAction.Area] = new HotkeyBinding(21, HotkeyModifiers.Command | HotkeyModifiers.Shift, "4");
        bindings[CaptureAction.Fullscreen] = new HotkeyBinding(21, HotkeyModifiers.Command, "4");
        Assert.Empty(bindings.Conflicts);
    }

    [Fact]
    [Unit]
    public void ConflictingActionLookupPointsAtTheClashingSibling()
    {
        var bindings = new HotkeyBindings();
        var chord = new HotkeyBinding(21, HotkeyModifiers.Command | HotkeyModifiers.Shift, "4");
        bindings[CaptureAction.Area] = chord;

        Assert.Equal(CaptureAction.Area, bindings.ConflictingAction(chord, excluding: CaptureAction.Fullscreen));
        Assert.Null(bindings.ConflictingAction(chord, excluding: CaptureAction.Area));
    }

    [Fact]
    [Unit]
    public void AChordSavedWithShiftsSymbolReadsAsItsDigit()
    {
        Assert.Equal("⇧⌘4", new HotkeyBinding(21, HotkeyModifiers.Shift | HotkeyModifiers.Command, "$").DisplayString);
        Assert.Equal("⌥⇧0", new HotkeyBinding(29, HotkeyModifiers.Shift | HotkeyModifiers.Option, ")").DisplayString);
        Assert.Equal("⌘$", new HotkeyBinding(21, HotkeyModifiers.Command, "$").DisplayString);
    }
}
