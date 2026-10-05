// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Lightshot.App.Views.History;

/// <summary>
/// Interface for sending files to the Recycle Bin.
/// P5a implements the Windows-specific IFileOperation RecycleBin in parallel.
/// </summary>
public interface IRecycleBin
{
    void DeleteToRecycleBin(string filePath);
}

/// <summary>
/// Default implementation of IRecycleBin using Shell API SHFileOperation.
/// </summary>
public class DefaultRecycleBin : IRecycleBin
{
    private const int FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT = 0x0004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCTW
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCTW lpFileOp);

    public void DeleteToRecycleBin(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

        try
        {
            var shf = new SHFILEOPSTRUCTW
            {
                wFunc = FO_DELETE,
                pFrom = filePath + '\0' + '\0',
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT
            };
            int res = SHFileOperationW(ref shf);
            if (res != 0 && File.Exists(filePath))
            {
                File.Delete(filePath);
            }
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
