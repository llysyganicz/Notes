using System;
using System.IO;

namespace Notes.Core.Services;

public sealed class PathGuard : IPathGuard
{
    private readonly ISettingsService _settingsService;

    public PathGuard(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public void EnsureWithinWorkspace(string absolutePath)
    {
        var root = _settingsService.CurrentWorkspacePath;
        if (string.IsNullOrEmpty(root))
            throw new PathContainmentException(
                "No workspace root is configured; path containment check cannot run.");

        var canonicalRoot = Path.GetFullPath(root);
        if (!canonicalRoot.EndsWith(Path.DirectorySeparatorChar))
            canonicalRoot += Path.DirectorySeparatorChar;

        var canonicalPath = Path.GetFullPath(absolutePath);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!canonicalPath.StartsWith(canonicalRoot, comparison))
            throw new PathContainmentException(
                $"Path '{absolutePath}' is outside the workspace root '{root}'.");
    }
}
