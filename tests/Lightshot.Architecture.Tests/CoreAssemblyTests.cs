using System.Reflection;
using System.Xml.Linq;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Architecture.Tests;

public class CoreAssemblyTests
{
    [Fact]
    [Unit]
    public void HasNoPackageOrWindowsReferences()
    {
        // 1. Verify project file
        string projectPath = FindRepoRelativePath("src/Lightshot.Core/Lightshot.Core.csproj");
        Assert.True(File.Exists(projectPath), $"Project file not found: {projectPath}");

        var doc = XDocument.Load(projectPath);
        var packageRefs = doc.Descendants("PackageReference").ToList();
        Assert.Empty(packageRefs);

        var projectRefs = doc.Descendants("ProjectReference").ToList();
        Assert.Empty(projectRefs);

        var tfm = doc.Descendants("TargetFramework").FirstOrDefault()?.Value.Trim();
        Assert.Equal("net10.0", tfm);

        // 2. Verify runtime assembly references
        var coreAssembly = typeof(CoreMarker).Assembly;
        var referencedAssemblies = coreAssembly.GetReferencedAssemblies();

        string[] forbiddenPrefixes =
        [
            "System.Windows",
            "Windows",
            "Microsoft.Win32",
            "System.Drawing",
            "DirectX",
            "Vortice",
            "SkiaSharp",
            "HarfBuzzSharp",
            "FlaUI"
        ];

        foreach (var refAsm in referencedAssemblies)
        {
            foreach (string forbidden in forbiddenPrefixes)
            {
                Assert.False(
                    refAsm.Name?.StartsWith(forbidden, StringComparison.OrdinalIgnoreCase) == true,
                    $"Lightshot.Core must not reference '{refAsm.Name}' (matches forbidden '{forbidden}')");
            }
        }
    }

    private static string FindRepoRelativePath(string relativePath)
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(current, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return candidate;
            }
            if (File.Exists(Path.Combine(current, "Lightshot.slnx")) || File.Exists(Path.Combine(current, "global.json")))
            {
                return Path.Combine(current, relativePath.Replace('/', Path.DirectorySeparatorChar));
            }
            current = Directory.GetParent(current)?.FullName ?? string.Empty;
        }
        return Path.GetFullPath(relativePath);
    }
}
