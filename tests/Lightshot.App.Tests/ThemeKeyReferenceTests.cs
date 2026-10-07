// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Media;
using System.Xml.Linq;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.App.Tests;

public class ThemeKeyReferenceTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Lightshot.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Lightshot.slnx not found above test output directory.");
    }

    [Fact]
    [Unit]
    public void EveryThemeKeyIsDefined()
    {
        string root = RepoRoot();
        string appDir = Path.Combine(root, "src", "Lightshot.App");

        var definedKeys = new HashSet<string>(StringComparer.Ordinal);
        var sourceFiles = new[]
        {
            Path.Combine(appDir, "Theming", "Themes", "Light.xaml"),
            Path.Combine(appDir, "Theming", "Themes", "Dark.xaml"),
            Path.Combine(appDir, "Theming", "Styles", "Controls.xaml")
        };

        var keyDefRegex = new Regex(@"x:Key=""(Theme\.\w+)""", RegexOptions.Compiled);
        foreach (var file in sourceFiles)
        {
            if (File.Exists(file))
            {
                string text = File.ReadAllText(file);
                foreach (Match m in keyDefRegex.Matches(text))
                {
                    definedKeys.Add(m.Groups[1].Value);
                }
            }
        }

        var refRegex = new Regex(@"(?:Dynamic|Static)Resource\s+(Theme\.\w+)", RegexOptions.Compiled);
        var missingReferences = new List<string>();

        foreach (var xamlFile in Directory.EnumerateFiles(appDir, "*.xaml", SearchOption.AllDirectories))
        {
            string[] lines = File.ReadAllLines(xamlFile);
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Match m in refRegex.Matches(lines[i]))
                {
                    string key = m.Groups[1].Value;
                    if (!definedKeys.Contains(key))
                    {
                        string relative = Path.GetRelativePath(root, xamlFile);
                        missingReferences.Add($"{relative}:{i + 1} references undefined key '{key}'");
                    }
                }
            }
        }

        Assert.True(missingReferences.Count == 0,
            $"Found {missingReferences.Count} undefined Theme key references:\n" + string.Join("\n", missingReferences));
    }

    // Every app-owned dotted key (Button.*, Font.*, Space.*, Text.*, ...) must be defined in some app
    // XAML file. A DynamicResource to a missing key silently falls back to the default value, which
    // is how the recording icons lost their icon font and rendered as tofu.
    [Fact]
    [Unit]
    public void EveryDottedResourceKeyIsDefined()
    {
        string root = RepoRoot();
        string appDir = Path.Combine(root, "src", "Lightshot.App");
        var xamlFiles = Directory.EnumerateFiles(appDir, "*.xaml", SearchOption.AllDirectories).ToList();

        var keyDefRegex = new Regex(@"x:Key=""([A-Za-z]\w*(?:\.\w+)+)""", RegexOptions.Compiled);
        var definedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in xamlFiles)
        {
            foreach (Match m in keyDefRegex.Matches(File.ReadAllText(file)))
            {
                definedKeys.Add(m.Groups[1].Value);
            }
        }

        var refRegex = new Regex(@"(?:Dynamic|Static)Resource\s+([A-Za-z]\w*(?:\.\w+)+)", RegexOptions.Compiled);
        var missingReferences = new List<string>();
        foreach (var file in xamlFiles)
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Match m in refRegex.Matches(lines[i]))
                {
                    if (!definedKeys.Contains(m.Groups[1].Value))
                    {
                        missingReferences.Add($"{Path.GetRelativePath(root, file)}:{i + 1} references undefined key '{m.Groups[1].Value}'");
                    }
                }
            }
        }

        Assert.True(missingReferences.Count == 0,
            $"Found {missingReferences.Count} undefined resource key references:\n" + string.Join("\n", missingReferences));
    }

    // The icon glyphs used by the recording views (XAML entities and code-behind \uEXXX literals)
    // must all exist in the first installed family of the shared Font.SegoeFluentIcons resource.
    [Fact]
    [Unit]
    public void RecordingIconGlyphsResolveInTheIconFont()
    {
        string root = RepoRoot();
        string appDir = Path.Combine(root, "src", "Lightshot.App");

        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var controls = XDocument.Load(Path.Combine(appDir, "Theming", "Styles", "Controls.xaml"));
        string? familySource = controls.Root!.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "FontFamily" && (string?)e.Attribute(x + "Key") == "Font.SegoeFluentIcons")
            ?.Value.Trim();
        Assert.False(string.IsNullOrEmpty(familySource), "Controls.xaml must define the Font.SegoeFluentIcons FontFamily resource.");

        var codepoints = new SortedSet<int>();
        var entityRegex = new Regex(@"&#x([EF][0-9A-Fa-f]{3});", RegexOptions.Compiled);
        var escapeRegex = new Regex(@"\\u([EF][0-9A-Fa-f]{3})", RegexOptions.Compiled);
        string recordingDir = Path.Combine(appDir, "Views", "Recording");
        foreach (var file in Directory.EnumerateFiles(recordingDir, "*.xaml"))
        {
            foreach (Match m in entityRegex.Matches(File.ReadAllText(file)))
            {
                codepoints.Add(Convert.ToInt32(m.Groups[1].Value, 16));
            }
        }
        foreach (var file in Directory.EnumerateFiles(recordingDir, "*.cs"))
        {
            foreach (Match m in escapeRegex.Matches(File.ReadAllText(file)))
            {
                codepoints.Add(Convert.ToInt32(m.Groups[1].Value, 16));
            }
        }
        Assert.NotEmpty(codepoints);

        var missing = new List<string>();
        string? resolvedFamily = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var installed = new HashSet<string>(Fonts.SystemFontFamilies.Select(f => f.Source), StringComparer.OrdinalIgnoreCase);
                resolvedFamily = familySource!.Split(',').Select(n => n.Trim()).FirstOrDefault(installed.Contains);
                if (resolvedFamily == null)
                {
                    return;
                }

                var typeface = new Typeface(new FontFamily(resolvedFamily), System.Windows.FontStyles.Normal,
                    System.Windows.FontWeights.Normal, System.Windows.FontStretches.Normal);
                Assert.True(typeface.TryGetGlyphTypeface(out var glyphs), $"No glyph typeface for {resolvedFamily}.");
                foreach (int cp in codepoints)
                {
                    if (!glyphs.CharacterToGlyphMap.ContainsKey(cp))
                    {
                        missing.Add($"U+{cp:X4}");
                    }
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
        {
            throw new InvalidOperationException("Glyph check failed on the STA thread.", failure);
        }
        Assert.True(resolvedFamily != null, $"None of '{familySource}' is installed.");
        Assert.True(missing.Count == 0, $"{resolvedFamily} lacks recording icon glyphs: {string.Join(", ", missing)}");
    }
}
