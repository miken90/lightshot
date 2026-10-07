// MIT License, Copyright (c) 2026 Viet Le
using System;
using System.IO;
using System.Linq;

namespace Lightshot.Core;

public partial class HistoryStore
{
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    // Screenshots written before history encoded PNG hold raw pixels nothing can show; the user chose to drop them.
    // Purge only Screenshot entries whose ImageFile ends in .png (LoadIndex defaults missing kind to Screenshot).
    private void PurgeLegacyRawScreenshots()
    {
        var legacy = _entries.Where(e =>
            e.Kind == CaptureKind.Screenshot &&
            e.ImageFile.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
            !IsPng(Path.Combine(_directory, e.ImageFile))).ToList();
        if (legacy.Count == 0) return;
        foreach (var e in legacy) { DeleteFiles(e); _entries.Remove(e); }
        Persist();
    }

    private static bool IsPng(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> head = stackalloc byte[8];
            return fs.Read(head) == 8 && head.SequenceEqual(PngSignature);
        }
        catch { return false; }
    }
}
