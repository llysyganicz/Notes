namespace Notes.Core.Models;

public sealed record AppSettings(string? WorkspacePath)
{
    public static AppSettings Empty { get; } = new(WorkspacePath: null);
}
