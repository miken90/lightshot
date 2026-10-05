using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Architecture.Tests;

public class NoticeFileTests
{
    private const string ExpectedUpstreamSha = "b54a970924e10d5321465ba6ff4816b919afcc61";
    private const string ExpectedCopyright = "Copyright (c) 2026 Viet Le";
    private const string ExpectedRepo = "https://github.com/lethanhvietctt5/lightshot";

    [Fact]
    [Unit]
    public void ContainsUpstreamShaAndCopyright()
    {
        string noticePath = FindRepoRelativePath("NOTICE");
        Assert.True(File.Exists(noticePath), $"NOTICE file not found at: {noticePath}");

        string content = File.ReadAllText(noticePath);

        Assert.Contains(ExpectedUpstreamSha, content);
        Assert.Contains(ExpectedCopyright, content);
        Assert.Contains(ExpectedRepo, content);
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
