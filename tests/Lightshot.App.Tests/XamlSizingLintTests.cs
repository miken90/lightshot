// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.App.Tests;

public class XamlSizingLintTests
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
    public void ViewsConformToSizingAndStructureRules()
    {
        string root = RepoRoot();
        string viewsDir = Path.Combine(root, "src", "Lightshot.App", "Views");
        var violations = new List<string>();

        string[] bannedPaddings = ["8,2,8,2", "6,2,6,2", "12,2,12,2"];

        foreach (var file in Directory.EnumerateFiles(viewsDir, "*.xaml", SearchOption.AllDirectories))
        {
            string relPath = Path.GetRelativePath(root, file).Replace('\\', '/');
            XDocument doc;
            try
            {
                using var reader = XmlReader.Create(file);
                doc = XDocument.Load(reader, LoadOptions.SetLineInfo);
            }
            catch (Exception ex)
            {
                violations.Add($"{relPath}: Failed to parse XML: {ex.Message}");
                continue;
            }

            // Rule (e): No Background="#..." on Window roots in Views
            var rootEl = doc.Root;
            if (rootEl != null && rootEl.Name.LocalName == "Window")
            {
                var bgAttr = rootEl.Attribute("Background");
                if (bgAttr != null && bgAttr.Value.Trim().StartsWith('#'))
                {
                    IXmlLineInfo lineInfo = bgAttr;
                    int line = lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1;
                    violations.Add($"{relPath}:{line} Window root hardcodes hex Background '{bgAttr.Value}'");
                }
            }

            foreach (var el in doc.Descendants())
            {
                IXmlLineInfo lineInfo = el;
                int line = lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1;
                string localName = el.Name.LocalName;

                // Rule (a): No <Button or <ToggleButton element with a Height below 32 unless explicitly permitted style or file
                if (localName is "Button" or "ToggleButton")
                {
                    var heightAttr = el.Attribute("Height");
                    if (heightAttr != null && double.TryParse(heightAttr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double height))
                    {
                        if (height < 32.0)
                        {
                            var styleAttr = el.Attribute("Style")?.Value ?? string.Empty;
                            bool isAllowed = styleAttr.Contains("Button.Icon")
                                || styleAttr.Contains("PillButtonStyle")
                                || styleAttr.Contains("CircleCornerButtonStyle")
                                || styleAttr.Contains("SwatchButton")
                                || styleAttr.Contains("ToolButtonStyle")
                                || styleAttr.Contains("SegmentButtonStyle")
                                || relPath.EndsWith("CardWindow.xaml", StringComparison.OrdinalIgnoreCase)
                                || relPath.EndsWith("PinWindow.xaml", StringComparison.OrdinalIgnoreCase);

                            if (!isAllowed)
                            {
                                violations.Add($"{relPath}:{line} <{localName}> has Height={height} (< 32) without allowed style or file");
                            }
                        }
                    }
                }

                // Rule (b): No <Style TargetType="Border"> inside <Button.Resources>
                if (localName == "Style" && el.Attribute("TargetType")?.Value == "Border")
                {
                    if (el.Parent != null && el.Parent.Name.LocalName == "Button.Resources")
                    {
                        violations.Add($"{relPath}:{line} <Style TargetType=\"Border\"> found inside <Button.Resources>");
                    }
                }

                // Rule (c): No Padding="8,2,8,2", "6,2,6,2" or "12,2,12,2"
                var padAttr = el.Attribute("Padding");
                if (padAttr != null)
                {
                    string normalized = padAttr.Value.Replace(" ", "");
                    if (bannedPaddings.Contains(normalized))
                    {
                        violations.Add($"{relPath}:{line} Banned padding '{padAttr.Value}' found");
                    }
                }

                // Rule (d): No GroupBox in Views/Settings
                if (localName == "GroupBox" && relPath.Contains("Views/Settings/"))
                {
                    violations.Add($"{relPath}:{line} <GroupBox> is forbidden in Settings");
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"Found {violations.Count} XAML sizing/lint violations:\n" + string.Join("\n", violations));
    }
}
