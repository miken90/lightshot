// MIT License, Copyright (c) 2026 Viet Le

using System;
using Lightshot.Core;

namespace Lightshot.App.Views.QuickAccess;

/// <summary>
/// Interface for a Quick Access card window, enabling decoupled testing and animation.
/// </summary>
public interface ICardWindow
{
    Guid Id { get; }
    CardViewModel ViewModel { get; }
    Rect Frame { get; set; }

    /// <summary>
    /// Moves the window onto the monitor that holds <paramref name="physicalWorkArea"/> and returns the
    /// device pixels per DIP that WPF then applies to its Left/Top/Width/Height.
    /// </summary>
    double MoveOntoDisplay(Rect physicalWorkArea);
    void ShowCard(Rect initialFrame, Rect targetFrame, bool animate);
    void CloseCard(bool animated = true);
    event EventHandler? Closed;
}
