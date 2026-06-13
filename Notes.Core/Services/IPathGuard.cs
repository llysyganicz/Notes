namespace Notes.Core.Services;

public interface IPathGuard
{
    void EnsureWithinWorkspace(string absolutePath);
}
