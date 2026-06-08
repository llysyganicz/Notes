namespace Notes.Services;

public interface IPathGuard
{
    void EnsureWithinWorkspace(string absolutePath);
}
