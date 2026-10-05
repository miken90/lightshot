// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Diagnostics;
using System.Text;
using Lightshot.Core;
using Lightshot.Platform.Windows.Clipboard;
using Lightshot.Rendering;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class ClipboardSinkTests
{
    [Fact]
    [Desktop]
    public void CopiesPngAndDib()
    {
        var codec = new SkiaImageCodec();
        var sink = new ClipboardImageSink(codec);

        // 16x16 test image (BGRA)
        int width = 16;
        int height = 16;
        byte[] pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 120;     // B
            pixels[i + 1] = 200; // G
            pixels[i + 2] = 50;  // R
            pixels[i + 3] = 255; // A
        }
        var image = new RenderedImage(width, height, pixels);

        // Copy to clipboard
        sink.CopyToClipboard(image);

        // Second reader: an independent external PowerShell process inspecting clipboard formats
        string script = @"
$code = @'
using System;
using System.Runtime.InteropServices;
public class ClipReader {
    [DllImport(""user32.dll"")] public static extern bool OpenClipboard(IntPtr h);
    [DllImport(""user32.dll"")] public static extern bool CloseClipboard();
    [DllImport(""user32.dll"")] public static extern uint EnumClipboardFormats(uint format);
    [DllImport(""user32.dll"", CharSet = CharSet.Unicode)] public static extern uint RegisterClipboardFormatW(string name);
    public static string Inspect() {
        if (!OpenClipboard(IntPtr.Zero)) return ""FAIL_OPEN"";
        try {
            uint pngFmt = RegisterClipboardFormatW(""PNG"");
            bool hasPng = false;
            bool hasDib = false;
            uint f = 0;
            while ((f = EnumClipboardFormats(f)) != 0) {
                if (f == 17) hasDib = true;
                if (f == pngFmt) hasPng = true;
            }
            return ""PNG="" + hasPng + "";DIBV5="" + hasDib;
        } finally {
            CloseClipboard();
        }
    }
}
'@
Add-Type -TypeDefinition $code
[ClipReader]::Inspect()
";

        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -NonInteractive -EncodedCommand " + encoded,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        Assert.NotNull(process);
        string output = process.StandardOutput.ReadToEnd().Trim();
        string error = process.StandardError.ReadToEnd().Trim();
        process.WaitForExit(15000);

        Assert.Equal(0, process.ExitCode);
        Assert.Contains("PNG=True", output);
        Assert.Contains("DIBV5=True", output);
    }
}
