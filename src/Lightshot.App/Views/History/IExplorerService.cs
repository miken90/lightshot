// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Diagnostics;
using System.IO;

namespace Lightshot.App.Views.History;

/// <summary>
/// Interface for revealing files in Windows Explorer.
/// P5a implements Windows/Explorer.cs in parallel.
/// </summary>
public interface IExplorerService
{
    void RevealInExplorer(string filePath);
}

/// <summary>
/// Default implementation of IExplorerService launching explorer.exe /select,path.
/// </summary>
public class DefaultExplorerService : IExplorerService
{
    public void RevealInExplorer(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{filePath}\"",
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch
        {
            // Ignore explorer launch failures
        }
    }
}
