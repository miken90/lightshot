// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
}
