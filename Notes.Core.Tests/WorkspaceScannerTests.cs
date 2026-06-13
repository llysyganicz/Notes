using System;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Notes.Core.Services;
using Xunit;

namespace Notes.Core.Tests;

public sealed class WorkspaceScannerTests
{
    private readonly MockFileSystem _fs = new();
    private readonly WorkspaceScanner _scanner;
    private readonly string _tempDir = "/workspace";

    public WorkspaceScannerTests()
    {
        _scanner = new WorkspaceScanner(_fs);
        _fs.Directory.CreateDirectory(_tempDir);
    }

    private void TouchFile(string relativePath)
    {
        var full = Path.Combine(_tempDir, relativePath);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
        {
            _fs.Directory.CreateDirectory(dir);
        }

        _fs.File.WriteAllText(full, string.Empty);
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
