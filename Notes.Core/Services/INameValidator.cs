namespace Notes.Core.Services;

public interface INameValidator
{
    NoteNameResult ValidateNoteName(string rawInput, string workspaceAbsolutePath, string parentRelativePath);

    NoteNameResult ValidateFolderName(string rawInput, string workspaceAbsolutePath, string parentRelativePath);
}

public abstract record NoteNameResult
{
    private NoteNameResult() { }

    public sealed record Success(string FileName, string AbsolutePath) : NoteNameResult;

    public sealed record Failure(string Error) : NoteNameResult;
}
