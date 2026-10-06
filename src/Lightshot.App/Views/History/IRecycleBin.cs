// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using Lightshot.Platform.Windows.Recycle;

namespace Lightshot.App.Views.History;

/// <summary>
/// Interface for sending files to the Recycle Bin.
/// </summary>
public interface IRecycleBin
{
    void DeleteToRecycleBin(string filePath);
}

/// <summary>
/// Default implementation of IRecycleBin adapting over P5a's RecycleBin service.
/// </summary>
public class DefaultRecycleBin : IRecycleBin
{
    public void DeleteToRecycleBin(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || (!File.Exists(filePath) && !Directory.Exists(filePath))) return;

        try
        {
            RecycleBin.Delete(filePath);
        }
        catch
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }
}
