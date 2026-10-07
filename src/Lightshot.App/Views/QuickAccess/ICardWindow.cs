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
    /// Device pixels per DIP that WPF applies to this window's Left/Top/Width/Height.
    /// </summary>
    double DeviceScale { get; }
    void ShowCard(Rect initialFrame, Rect targetFrame, bool animate);
    void CloseCard(bool animated = true);
    event EventHandler? Closed;
}
