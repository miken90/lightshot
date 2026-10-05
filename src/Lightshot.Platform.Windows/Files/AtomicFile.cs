// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;

namespace Lightshot.Platform.Windows.Files;

/// <summary>
/// Writes files atomically to disk using a temporary file in the destination directory
/// followed by an atomic rename/replace (MoveFileEx / ReplaceFile).
/// </summary>
public static class AtomicFile
{
    /// <summary>
    /// Writes bytes to a destination path atomically.
    /// </summary>
    public static void WriteAllBytes(string destinationPath, byte[] bytes)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("Destination path cannot be empty", nameof(destinationPath));
        }

        string fullPath = Path.GetFullPath(destinationPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string fileName = Path.GetFileName(fullPath);
        string tempPath = Path.Combine(directory ?? "", $".{fileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllBytes(tempPath, bytes);

            if (File.Exists(fullPath))
            {
                File.Replace(tempPath, fullPath, null);
            }
            else
            {
                File.Move(tempPath, fullPath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                }
            }
        }
    }

    /// <summary>
    /// Writes text to a destination path atomically.
    /// </summary>
    public static void WriteAllText(string destinationPath, string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        WriteAllBytes(destinationPath, bytes);
    }
}
