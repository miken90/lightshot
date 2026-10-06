// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using Lightshot.Platform.Windows.Windows;

namespace Lightshot.App.Views.History;

/// <summary>
/// Interface for revealing files in Windows Explorer.
/// </summary>
public interface IExplorerService
{
    void RevealInExplorer(string filePath);
}

/// <summary>
/// Default implementation of IExplorerService adapting over P5a's Explorer service.
/// </summary>
public class DefaultExplorerService : IExplorerService
{
    public void RevealInExplorer(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

        try
        {
            Explorer.Reveal(filePath);
        }
        catch
        {
            // Ignore explorer launch failures
        }
    }
}
