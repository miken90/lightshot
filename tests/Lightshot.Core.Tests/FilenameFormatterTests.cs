// MIT License, Copyright (c) 2026 Viet Le

using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Core.Tests;

public class FilenameFormatterTests
{
    [Fact]
    [Unit]
    public void StripsWindowsForbiddenCharacters()
    {
        // Windows forbidden chars: < > : " / \ | ? *
        string? sanitized = FilenameFormatter.Sanitized("test<one>two:three\"four/five\\six|seven?eight*nine");
        Assert.Equal("test-one-two-three-four-five-six-seven-eight-nine", sanitized);

        // Control characters (< 0x20) are stripped
        string? withControls = FilenameFormatter.Sanitized("clean\u0001text\u001fhere");
        Assert.Equal("cleantexthere", withControls);

        // Surrounding whitespace and dots are trimmed
        string? trimmed = FilenameFormatter.Sanitized("  ..my-file..  ");
        Assert.Equal("my-file", trimmed);

        // Reserved DOS device names are prefixed with underscore
        Assert.Equal("_CON", FilenameFormatter.Sanitized("CON"));
        Assert.Equal("_aux.png", FilenameFormatter.Sanitized("aux.png"));
        Assert.Equal("_nul", FilenameFormatter.Sanitized("nul"));
        Assert.Equal("_COM1.txt", FilenameFormatter.Sanitized("COM1.txt"));

        // Blank or all-forbidden strings return null
        Assert.Null(FilenameFormatter.Sanitized(""));
        Assert.Null(FilenameFormatter.Sanitized("   "));
        Assert.Null(FilenameFormatter.Sanitized("..."));
    }
}
