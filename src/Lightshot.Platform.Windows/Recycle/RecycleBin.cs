// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Lightshot.Platform.Windows.Recycle;

/// <summary>
/// Provides shell file deletion to the Windows Recycle Bin via IFileOperation.
/// Falls back to standard file deletion if Recycle Bin is unavailable (e.g. network drives).
/// </summary>
public static class RecycleBin
{
    private const uint FOF_ALLOWUNDO = 0x0040;
    private const uint FOF_NOCONFIRMATION = 0x0010;
    private const uint FOF_NOERRORUI = 0x0400;
    private const uint FOF_SILENT = 0x0004;

    private static readonly Guid IShellItemGuid = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
    private static readonly Guid FileOperationClsid = new("3ad05575-8857-4850-9277-11b85bdb8e09");

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = false)]
    [return: MarshalAs(UnmanagedType.Interface)]
    private static extern object SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid);

    /// <summary>
    /// Moves a file or folder to the Windows Recycle Bin.
    /// </summary>
    /// <param name="path">Absolute or relative path of the file/folder to delete.</param>
    public static void Delete(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be null or empty.", nameof(path));
        }

        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            throw new FileNotFoundException("Target path to recycle does not exist.", fullPath);
        }

        try
        {
            DeleteViaIFileOperation(fullPath);
        }
        catch
        {
            // Fallback for network drives or unsupported filesystems
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
            else if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, true);
            }
        }
    }

    private static void DeleteViaIFileOperation(string fullPath)
    {
        Type? fileOpType = Type.GetTypeFromCLSID(FileOperationClsid);
        if (fileOpType == null)
        {
            throw new InvalidOperationException("Failed to resolve FileOperation COM type.");
        }

        var fileOpObj = Activator.CreateInstance(fileOpType);
        if (fileOpObj is not IFileOperation fileOp)
        {
            throw new InvalidOperationException("Failed to cast to IFileOperation.");
        }

        try
        {
            fileOp.SetOperationFlags(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT);

            object shellItem = SHCreateItemFromParsingName(fullPath, IntPtr.Zero, IShellItemGuid);
            fileOp.DeleteItem(shellItem, null);
            fileOp.PerformOperations();

            if (fileOp.GetAnyOperationsAborted())
            {
                throw new OperationCanceledException("Recycle operation was aborted.");
            }
        }
        finally
        {
            if (Marshal.IsComObject(fileOpObj))
            {
                Marshal.ReleaseComObject(fileOpObj);
            }
        }
    }

    [ComImport]
    [Guid("947aab5f-0a4c-4423-b60b-6007b025449f")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        uint Advise(object pfops, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOperationFlags(uint dwOperationFlags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string pszMessage);
        void SetProgressDialog(object popd);
        void SetProperties(object pproparray);
        void SetOwnerWindow(IntPtr hwndOwner);
        void ApplyPropertiesToItem(object psi);
        void ApplyPropertiesToItems(object punkItems);
        void RenameItem(object psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, object? pfopsItem);
        void RenameItems(object pUnkItems, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
        void MoveItem(object psiItem, object psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, object? pfopsItem);
        void MoveItems(object punkItems, object psiDestinationFolder);
        void CopyItem(object psiItem, object psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, object? pfopsItem);
        void CopyItems(object punkItems, object psiDestinationFolder);
        void DeleteItem(object psiItem, object? pfopsItem);
        void DeleteItems(object punkItems);
        void NewItem(object psiDestinationFolder, uint dwFileAttributes, [MarshalAs(UnmanagedType.LPWStr)] string pszName, [MarshalAs(UnmanagedType.LPWStr)] string? pszTemplateName, object? pfopsItem);
        void PerformOperations();
        [return: MarshalAs(UnmanagedType.Bool)]
        bool GetAnyOperationsAborted();
    }
}
