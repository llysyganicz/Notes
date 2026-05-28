namespace Notes.Models;

public sealed record AppSettings(string? WorkspacePath)
{
    public static AppSettings Empty { get; } = new(WorkspacePath: null);
}
