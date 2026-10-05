// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.IO;

namespace Lightshot.Core.Tests.FakeServices;

public class FakeMediaSink : IMediaSink
{
    public bool SaveFails { get; set; }
    public bool TrashFails { get; set; }

    public List<string> Copied { get; } = [];
    public List<(string Source, string Destination)> Saves { get; } = [];
    public List<string> Trashed { get; } = [];
    public List<string> Deleted { get; } = [];
    public List<(string Source, string Destination)> Copies { get; } = [];

    public void CopyFile(string path)
    {
        Copied.Add(path);
    }

    public void Save(string sourcePath, string destinationPath)
    {
        if (SaveFails) throw new IOException("Save failed");
        Saves.Add((sourcePath, destinationPath));
    }

    public void Trash(string path)
    {
        if (TrashFails) throw new IOException("Trash failed");
        Trashed.Add(path);
    }

    public void Delete(string path)
    {
        Deleted.Add(path);
    }

    public void Copy(string sourcePath, string destinationPath)
    {
        if (SaveFails) throw new IOException("Copy failed");
        Copies.Add((sourcePath, destinationPath));
    }
}
