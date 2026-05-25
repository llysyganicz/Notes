using System;
using System.IO;
using System.Linq;
using Notes.Services;
using Xunit;

namespace Notes.Tests;

public sealed class WorkspaceScannerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WorkspaceScanner _scanner = new();

    public WorkspaceScannerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Notes_ScannerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private void TouchFile(string relativePath)
    {
        var full = Path.Combine(_tempDir, relativePath);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(full, string.Empty);
    }

    [Fact]
    public void Empty_directory_returns_empty_list()
    {
        var result = _scanner.ScanMarkdownFiles(_tempDir);

        Assert.Empty(result);
    }

    [Fact]
    public void Returns_only_markdown_files_at_flat_level()
    {
        TouchFile("a.md");
        TouchFile("b.txt");
        TouchFile("c.md");

        var result = _scanner.ScanMarkdownFiles(_tempDir);

        Assert.Equal(new[] { "a.md", "c.md" }, result);
    }

    [Fact]
    public void Recurses_into_subdirectories()
    {
        TouchFile("root.md");
        TouchFile("sub/inner.md");
        TouchFile("sub/deeper/leaf.md");

        var result = _scanner.ScanMarkdownFiles(_tempDir);

        Assert.Contains("root.md", result);
        Assert.Contains("sub/inner.md", result);
        Assert.Contains("sub/deeper/leaf.md", result);
    }

    [Fact]
    public void Uses_forward_slash_separator_regardless_of_os()
    {
        TouchFile("sub/inner.md");

        var result = _scanner.ScanMarkdownFiles(_tempDir);

        Assert.All(result, path => Assert.DoesNotContain('\\', path));
    }

    [Fact]
    public void Results_are_sorted_lexicographically()
    {
        TouchFile("zebra.md");
        TouchFile("apple.md");
        TouchFile("mango.md");

        var result = _scanner.ScanMarkdownFiles(_tempDir);

        Assert.Equal(result.OrderBy(p => p, StringComparer.Ordinal).ToArray(), result.ToArray());
    }

    [Fact]
    public void Recurses_into_dotfolders_but_skips_dotfiles()
    {
        TouchFile(".templates/visible.md");
        TouchFile(".templates/.hidden.md");

        var result = _scanner.ScanMarkdownFiles(_tempDir);

        Assert.Contains(".templates/visible.md", result);
        Assert.DoesNotContain(".templates/.hidden.md", result);
    }

    [Fact]
    public void Returns_empty_when_root_does_not_exist()
    {
        var result = _scanner.ScanMarkdownFiles(Path.Combine(_tempDir, "missing"));

        Assert.Empty(result);
    }
}
