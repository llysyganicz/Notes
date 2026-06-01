using Notes.Models;

namespace Notes.Messaging;

public sealed record WorkspaceChangedMessage(string WorkspacePath);

public sealed record NoteSelectedMessage(NoteTreeNode? Node);

public sealed record NoteDeletedMessage(string RelativePath);

public sealed record NewNoteRequestedMessage;

public sealed record TogglePreviewRequestedMessage;

public sealed record NoteSavedMessage(string RelativePath, string Content);

public sealed record SearchIndexStateChangedMessage(bool IsReady);
