// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.App.Tests;

public class ToolIconsTests
{
    private const string IconsUri = "pack://application:,,,/Lightshot.App;component/Resources/Icons/ToolIcons.xaml";
    private const string PreviewDirVariable = "LIGHTSHOT_ICON_PREVIEW_DIR";
    private static readonly int[] TraySizes = [16, 20, 24, 32, 40, 48];
    private static readonly int[] AppIconSizes = [16, 20, 24, 32, 40, 48, 64, 256];
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // Alpha below this is antialiasing haze, not ink.
    private const byte InkAlpha = 16;

    private static void RunInSta(Action action)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            action();
            return;
        }

        ExceptionDispatchInfo? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        captured?.Throw();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Lightshot.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Lightshot.slnx not found above the test output directory.");
    }

    private static string AppFile(params string[] parts) =>
        Path.Combine([RepoRoot(), "src", "Lightshot.App", .. parts]);

    // The palette XAML is the contract: every Icon.* key it references must exist in the dictionary.
    private static string[] PaletteKeys()
    {
        var xaml = File.ReadAllText(AppFile("Views", "Editor", "ToolPalette.xaml"));
        return Regex.Matches(xaml, @"\{DynamicResource (Icon\.\w+)\}")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToArray();
    }

    // Application's static constructor registers the pack:// web-request factory. The test process has
    // no Application, so without touching it the first pack:// load fails when this test runs first.
    private static ResourceDictionary LoadIcons()
    {
        _ = Application.Current;
        return new ResourceDictionary { Source = new Uri(IconsUri, UriKind.Absolute) };
    }

    // A fresh dictionary per call so each theme brush binds to its own copy of the shared pen.
    private static (DrawingImage Image, Grid Scope) Load(string key, Brush ink)
    {
        var scope = new Grid();
        scope.Resources.MergedDictionaries.Add(new ResourceDictionary { ["Icon.Brush"] = ink });
        scope.Resources.MergedDictionaries.Add(LoadIcons());
        var image = Assert.IsType<DrawingImage>(scope.Resources[key]);
        return (image, scope);
    }

    private static Grid Cell(string key, int size, Brush ink, Brush? background = null)
    {
        var (image, scope) = Load(key, ink);
        scope.Width = scope.Height = size;
        scope.Background = background;
        scope.Children.Add(new Image { Source = image, Stretch = Stretch.Uniform });
        return scope;
    }

    private static RenderTargetBitmap Rasterize(FrameworkElement element, int width, int height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        return bitmap;
    }

    private static byte[] Pixels(RenderTargetBitmap bitmap)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    private static byte[] RenderIcon(string key, int size, Brush? ink = null) =>
        Pixels(Rasterize(Cell(key, size, ink ?? Brushes.Black), size, size));

    private static (int MinX, int MinY, int MaxX, int MaxY, int Count) InkBounds(byte[] pixels, int size)
    {
        int minX = size, minY = size, maxX = -1, maxY = -1, count = 0;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (pixels[((y * size) + x) * 4 + 3] < InkAlpha)
                {
                    continue;
                }

                count++;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        }

        return (minX, minY, maxX, maxY, count);
    }

    [Fact]
    [Render]
    public void DictionaryHoldsEveryPaletteIcon()
    {
        RunInSta(() =>
        {
            var keys = PaletteKeys();
            Assert.Equal(12, keys.Length);

            var dictionary = LoadIcons();
            foreach (var key in keys)
            {
                Assert.IsType<DrawingImage>(dictionary[key]);
            }
        });
    }

    [Fact]
    [Render]
    public void EveryIconRendersInkAtBothSizes()
    {
        RunInSta(() =>
        {
            foreach (var key in PaletteKeys())
            {
                foreach (var size in new[] { 16, 24 })
                {
                    var bounds = InkBounds(RenderIcon(key, size), size);
                    Assert.True(bounds.Count >= size, $"{key} at {size}px has only {bounds.Count} ink pixels.");
                }
            }
        });
    }

    [Fact]
    [Render]
    public void EveryIconStaysInsideTheSafeMargin()
    {
        RunInSta(() =>
        {
            foreach (var key in PaletteKeys())
            {
                // 2 px margin on the 24 grid; scaled to 1.33 px at 16 px, so ink may start in pixel 1.
                foreach (var (size, margin) in new[] { (24, 2), (16, 1) })
                {
                    var b = InkBounds(RenderIcon(key, size), size);
                    var last = size - 1 - margin;
                    Assert.True(
                        b.MinX >= margin && b.MinY >= margin && b.MaxX <= last && b.MaxY <= last,
                        $"{key} at {size}px ink spans ({b.MinX},{b.MinY})-({b.MaxX},{b.MaxY}), outside {margin}..{last}.");
                }
            }
        });
    }

    [Fact]
    [Render]
    public void NoTwoIconsRenderIdentically()
    {
        RunInSta(() =>
        {
            foreach (var size in new[] { 16, 24 })
            {
                var seen = new Dictionary<string, string>();
                foreach (var key in PaletteKeys())
                {
                    var hash = Convert.ToHexString(SHA256.HashData(RenderIcon(key, size)));
                    Assert.False(seen.TryGetValue(hash, out var other), $"{key} and {other} render identically at {size}px.");
                    seen[hash] = key;
                }
            }
        });
    }

    [Fact]
    [Render]
    public void InkFollowsTheThemeBrush()
    {
        RunInSta(() =>
        {
            foreach (var key in PaletteKeys())
            {
                var pixels = RenderIcon(key, 24, Brushes.White);
                var bounds = InkBounds(pixels, 24);
                Assert.True(bounds.Count > 0, $"{key} is empty under a white theme brush.");

                // Premultiplied white ink has B == G == R == A on every pixel; any other colour is hard-coded.
                for (var i = 0; i < pixels.Length; i += 4)
                {
                    Assert.True(
                        pixels[i] == pixels[i + 3] && pixels[i + 1] == pixels[i + 3] && pixels[i + 2] == pixels[i + 3],
                        $"{key} has a pixel that is not the theme brush colour.");
                }
            }
        });
    }

    [Fact]
    [Unit]
    public void SourceHasNoHardCodedColour()
    {
        var xaml = File.ReadAllText(AppFile("Resources", "Icons", "ToolIcons.xaml"));
        Assert.DoesNotMatch(@"#[0-9A-Fa-f]{3,8}\b", xaml.Replace("<!--", "").Replace("-->", ""));
        Assert.DoesNotMatch("(Brush|Fill|Stroke)=\"(?!\\{DynamicResource Icon\\.Brush\\}|Transparent\")", xaml);
    }

    // Directory entries of an .ico: pixel width (0 means 256) and whether the payload is PNG data.
    private static List<(int Width, bool IsPng)> IconEntries(byte[] data)
    {
        Assert.Equal(0, BitConverter.ToUInt16(data, 0));
        Assert.Equal(1, BitConverter.ToUInt16(data, 2));
        var count = BitConverter.ToUInt16(data, 4);

        var entries = new List<(int, bool)>();
        for (var i = 0; i < count; i++)
        {
            var entry = 6 + (i * 16);
            var width = data[entry] == 0 ? 256 : data[entry];
            var height = data[entry + 1] == 0 ? 256 : data[entry + 1];
            Assert.Equal(width, height);

            var offset = BitConverter.ToInt32(data, entry + 12);
            entries.Add((width, data.AsSpan(offset, PngSignature.Length).SequenceEqual(PngSignature)));
        }

        return entries;
    }

    // Decodes every frame of an .ico with the real WPF decoder, as Pbgra32 pixels keyed by pixel width.
    private static Dictionary<int, (int Width, byte[] Pixels)> IconFrames(string path)
    {
        var decoder = new IconBitmapDecoder(
            new MemoryStream(File.ReadAllBytes(path)),
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frames = new Dictionary<int, (int, byte[])>();
        foreach (var frame in decoder.Frames)
        {
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
            var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
            converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
            frames[converted.PixelWidth] = (converted.PixelWidth, pixels);
        }

        return frames;
    }

    private static BitmapSource FrameImage(int width, byte[] pixels)
    {
        var bitmap = BitmapSource.Create(width, width, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    [Theory]
    [Render]
    [InlineData("light")]
    [InlineData("dark")]
    public void TrayIconHoldsSixSizes(string variant)
    {
        var dir = AppFile("Resources", "Icons", "Tray");
        Assert.True(File.Exists(Path.Combine(dir, $"tray-{variant}.svg")), "SVG source is missing next to the .ico.");

        var entries = IconEntries(File.ReadAllBytes(Path.Combine(dir, $"tray-{variant}.ico")));
        foreach (var (width, isPng) in entries)
        {
            Assert.True(isPng, $"{width}px entry is not PNG data.");
        }

        Assert.Equal(TraySizes, entries.Select(e => e.Width).OrderBy(w => w).ToArray());
    }

    [Fact]
    [Render]
    public void AppIconHoldsEightSizes()
    {
        var dir = AppFile("Resources", "Icons");
        Assert.True(File.Exists(Path.Combine(dir, "app.svg")), "SVG source is missing next to app.ico.");

        var entries = IconEntries(File.ReadAllBytes(Path.Combine(dir, "app.ico")));
        Assert.Equal(AppIconSizes, entries.Select(e => e.Width).OrderBy(w => w).ToArray());
        Assert.True(entries.Single(e => e.Width == 256).IsPng, "The 256px frame must be PNG-compressed.");
    }

    [Fact]
    [Render]
    public void AppIconFramesAreInkedAndDifferFromTheTrayGlyph()
    {
        RunInSta(() =>
        {
            var dir = AppFile("Resources", "Icons");
            var app = IconFrames(Path.Combine(dir, "app.ico"));
            var trays = new[] { "light", "dark" }.ToDictionary(v => v, v => IconFrames(Path.Combine(dir, "Tray", $"tray-{v}.ico")));
            Assert.Equal(AppIconSizes, app.Keys.OrderBy(k => k).ToArray());

            foreach (var (size, frame) in app)
            {
                var bounds = InkBounds(frame.Pixels, size);
                Assert.True(bounds.Count >= size, $"app.ico {size}px frame has only {bounds.Count} ink pixels.");

                // A full-colour tile covers most of the frame; a stray tray copy would leave it mostly empty.
                Assert.True(bounds.Count >= size * size / 2, $"app.ico {size}px frame is mostly transparent.");

                foreach (var (variant, tray) in trays)
                {
                    if (tray.TryGetValue(size, out var trayFrame))
                    {
                        Assert.False(
                            frame.Pixels.AsSpan().SequenceEqual(trayFrame.Pixels),
                            $"app.ico {size}px frame is identical to tray-{variant}.");
                    }
                }
            }
        });
    }

    // Art-review aid: with LIGHTSHOT_ICON_PREVIEW_DIR set, writes contact sheets rendered from the real XAML.
    [Fact]
    [Render]
    public void PreviewSheetsRenderWhenRequested()
    {
        var outDir = Environment.GetEnvironmentVariable(PreviewDirVariable);
        if (string.IsNullOrWhiteSpace(outDir))
        {
            return;
        }

        RunInSta(() =>
        {
            Directory.CreateDirectory(outDir);
            var themes = new[]
            {
                ("light", new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3)), new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20))),
                ("dark", new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)), new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0))),
            };

            foreach (var (name, background, ink) in themes)
            {
                foreach (var size in new[] { 16, 24, 48 })
                {
                    var pad = size / 2;
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Background = background };
                    foreach (var key in PaletteKeys())
                    {
                        var cell = Cell(key, size, ink);
                        cell.Margin = new Thickness(pad);
                        row.Children.Add(cell);
                    }

                    var width = PaletteKeys().Length * (size + (2 * pad));
                    var height = size + (2 * pad);
                    var bitmap = Rasterize(row, width, height);

                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(outDir, $"tool-icons-{name}-{size}px.png"));
                    encoder.Save(stream);
                }
            }
        });
    }

    // Art-review aid: with LIGHTSHOT_ICON_PREVIEW_DIR set, writes app.ico sheets decoded with the real WPF

    // decoder, each beside the tray glyph that suits the background.
    [Fact]
    [Render]
    public void AppIconPreviewSheetsRenderWhenRequested()
    {
        var outDir = Environment.GetEnvironmentVariable(PreviewDirVariable);
        if (string.IsNullOrWhiteSpace(outDir))
        {
            return;
        }

        RunInSta(() =>
        {
            Directory.CreateDirectory(outDir);
            var dir = AppFile("Resources", "Icons");
            var app = IconFrames(Path.Combine(dir, "app.ico"));
            var themes = new[]
            {
                ("light", Color.FromRgb(0xF3, 0xF3, 0xF3)),
                ("dark", Color.FromRgb(0x20, 0x20, 0x20)),
            };

            foreach (var (name, color) in themes)
            {
                var tray = IconFrames(Path.Combine(dir, "Tray", $"tray-{name}.ico"));
                foreach (var (size, zoom) in new[] { (16, 1), (24, 1), (32, 1), (48, 1), (256, 1), (16, 8), (24, 8) })
                {
                    // The tray has no frame above 48 px, so the 256 sheet sets its 48 px frame beside the icon.
                    var traySize = Math.Min(size, 48);
                    var pad = Math.Max(size / 2, 8) * zoom;
                    var appPx = size * zoom;
                    var trayPx = traySize * zoom;

                    var row = new StackPanel { Orientation = Orientation.Horizontal, Background = new SolidColorBrush(color) };
                    foreach (var (pixelWidth, pixels, px) in new[] { (size, app[size].Pixels, appPx), (traySize, tray[traySize].Pixels, trayPx) })
                    {
                        var image = new Image
                        {
                            Source = FrameImage(pixelWidth, pixels),
                            Width = px,
                            Height = px,
                            Stretch = Stretch.Fill,
                            Margin = new Thickness(pad),
                        };
                        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                        row.Children.Add(image);
                    }

                    var width = appPx + trayPx + (4 * pad);
                    var height = Math.Max(appPx, trayPx) + (2 * pad);
                    var bitmap = Rasterize(row, width, height);

                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    var suffix = zoom > 1 ? $"-x{zoom}" : string.Empty;
                    using var stream = File.Create(Path.Combine(outDir, $"app-icon-{name}-{size}px{suffix}.png"));
                    encoder.Save(stream);
                }
            }
        });
    }

    [Fact]
    [Render]
    public void ToolPaletteRendersIconsNotText()
    {
        RunInSta(() =>
        {
            var palette = new Lightshot.App.Views.Editor.ToolPalette();
            // Provide Icon.Brush and icons in the palette's resources if not in Application
            palette.Resources["Icon.Brush"] = Brushes.White;
            palette.Resources.MergedDictionaries.Add(LoadIcons());

            var buttons = new List<Button>();
            if (palette.Content is StackPanel sp)
            {
                foreach (var child in sp.Children)
                {
                    if (child is Button b) buttons.Add(b);
                }
            }

            Assert.Equal(12, buttons.Count);

            foreach (var button in buttons)
            {
                // Assert each button holds an Image, not ContentControl with text
                var image = Assert.IsType<Image>(button.Content);
                var drawingImage = Assert.IsType<DrawingImage>(image.Source);
                Assert.NotNull(drawingImage.Drawing);

                // ONE size, 16 or 24 (never 20)
                Assert.True(image.Width == 16 || image.Width == 24,
                    $"{button.Tag} has size {image.Width}, expected 16 or 24.");
                Assert.Equal(image.Width, image.Height);
                Assert.NotEqual(20, image.Width);

                // Assert non-empty ink rendered
                button.Resources["Icon.Brush"] = Brushes.White;
                button.Resources.MergedDictionaries.Add(LoadIcons());
                var pixels = Pixels(Rasterize(button, 32, 28));
                var bounds = InkBounds(pixels, 28);
                Assert.True(bounds.Count > 0, $"{button.Tag} rendered zero ink pixels.");
            }
        });
    }
}

