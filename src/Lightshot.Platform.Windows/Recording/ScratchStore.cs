using System;
using System.Collections.Generic;
using System.IO;
using Lightshot.Platform.Windows.Files;

namespace Lightshot.Platform.Windows.Recording;

/// <summary>
/// Manages temporary recording takes and remux artifacts in %LocalAppData%\Lightshot\Recordings.
/// </summary>
public sealed class ScratchStore
{
    public const string FragmentedExtension = ".frag.mp4";
    public const string PartialExtension = ".partial";
    public const string FinalMp4Extension = ".mp4";

    public string RootDirectory { get; }

    public ScratchStore(string? rootDirectory = null)
    {
        RootDirectory = rootDirectory ?? Path.Combine(
            KnownFolders.LocalApplicationData,
            "Lightshot",
            "Recordings");

        Directory.CreateDirectory(RootDirectory);
    }

    /// <summary>Generates a unique path for an in-progress fragmented take (.frag.mp4).</summary>
    public string CreateTakePath(string? prefix = null)
    {
        string name = $"{(string.IsNullOrWhiteSpace(prefix) ? "take" : prefix)}_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{FragmentedExtension}";
        return Path.Combine(RootDirectory, name);
    }

    /// <summary>Returns the temporary partial remux output path for a take.</summary>
    public string GetRemuxPartialPath(string takePath)
    {
        string baseName = Path.GetFileNameWithoutExtension(takePath);
        if (baseName.EndsWith(".frag", StringComparison.OrdinalIgnoreCase))
        {
            baseName = Path.GetFileNameWithoutExtension(baseName);
        }
        return Path.Combine(RootDirectory, $"{baseName}{FinalMp4Extension}{PartialExtension}");
    }

    /// <summary>Returns the final progressive MP4 path for a take.</summary>
    public string GetFinalMp4Path(string takePath)
    {
        string baseName = Path.GetFileNameWithoutExtension(takePath);
        if (baseName.EndsWith(".frag", StringComparison.OrdinalIgnoreCase))
        {
            baseName = Path.GetFileNameWithoutExtension(baseName);
        }
        return Path.Combine(RootDirectory, $"{baseName}{FinalMp4Extension}");
    }

    /// <summary>Enumerates unfinalized fragmented takes (*.frag.mp4).</summary>
    public IReadOnlyList<string> EnumerateTakes()
    {
        if (!Directory.Exists(RootDirectory)) return Array.Empty<string>();
        return Directory.GetFiles(RootDirectory, $"*{FragmentedExtension}");
    }

    /// <summary>Deletes stale *.partial files left from crashed remuxes or GIF encodes.</summary>
    public int CleanStalePartials()
    {
        if (!Directory.Exists(RootDirectory)) return 0;
        int deleted = 0;
        foreach (string file in Directory.GetFiles(RootDirectory, $"*{PartialExtension}*"))
        {
            try
            {
                File.Delete(file);
                deleted++;
            }
            catch
            {
                // Antivirus / indexing locks ignored during cleanup pass
            }
        }
        return deleted;
    }

    /// <summary>Deletes a fragmented take file when remuxing succeeds or when discarded.</summary>
    public bool DeleteTake(string takePath)
    {
        try
        {
            if (File.Exists(takePath))
            {
                File.Delete(takePath);
                return true;
            }
        }
        catch
        {
            // Best effort deletion
        }
        return false;
    }
}
