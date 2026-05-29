namespace Notes.Services;

public interface INewNoteNameValidator
{
    NoteNameResult Validate(string rawInput, string workspaceAbsolutePath, string parentRelativePath);
}

public abstract record NoteNameResult
{
    private NoteNameResult() { }

    public sealed record Success(string FileName) : NoteNameResult;

    public sealed record Failure(string Error) : NoteNameResult;
}
