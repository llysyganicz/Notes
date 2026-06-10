using System;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using Notes.Core.Services;
using Xunit;

namespace Notes.Core.Tests;

public sealed class WorkspaceScannerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WorkspaceScanner _scanner = new(new FileSystem());

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
    public void ScanMarkdownFiles_WhenDirectoryEmpty_ReturnsEmptyList()
    {
        var result = _scanner.ScanMarkdownFiles(_tempDir);

        Assert.Empty(result);
    }

    [Fact]
    public void ScanMarkdownFiles_WhenFlatDirectory_ReturnsOnlyMarkdownFiles()
    {
        TouchFile("a.md");
        TouchFile("b.txt");
        TouchFile("c.md");

        var result = _scanner.ScanMarkdownFiles(_tempDir);

        Assert.Equal(new[] { "a.md", "c.md" }, result);
    }

    [Fact]
    public void ScanMarkdownFiles_WhenSubdirectoriesPresent_ReturnsFilesRecursively()
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
    public void ScanMarkdownFiles_WhenCalled_ReturnsPathsWithForwardSlashSeparator()
    {
        TouchFile("sub/inner.md");

        var result = _scanner.ScanMarkdownFiles(_tempDir);

        Assert.All(result, path => Assert.DoesNotContain('\\', path));
    }

    [Fact]
    public void ScanMarkdownFiles_WhenCalled_ReturnsResultsSortedLexicographically()
    {
        TouchFile("zebra.md");
        TouchFile("apple.md");
        TouchFile("mango.md");

        var result = _scanner.ScanMarkdownFiles(_tempDir);

        Assert.Equal(result.OrderBy(p => p, StringComparer.Ordinal).ToArray(), result.ToArray());
    }

    [Fact]
    public void ScanMarkdownFiles_WhenDotfoldersAndDotfilesPresent_RecursesIntoDotfoldersAndSkipsDotfiles()
    {
        TouchFile(".templates/visible.md");
        TouchFile(".templates/.hidden.md");

        var result = _scanner.ScanMarkdownFiles(_tempDir);

        Assert.Contains(".templates/visible.md", result);
        Assert.DoesNotContain(".templates/.hidden.md", result);
    }

    [Fact]
    public void ScanMarkdownFiles_WhenRootMissing_ReturnsEmptyList()
    {
        var result = _scanner.ScanMarkdownFiles(Path.Combine(_tempDir, "missing"));

        Assert.Empty(result);
    }
}
