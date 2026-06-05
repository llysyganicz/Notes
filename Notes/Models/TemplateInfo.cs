namespace Notes.Models;

/// <summary>
/// A template available under the workspace's top-level <c>.templates/</c> folder.
/// <see cref="RelativePath"/> is the scanner-relative path (e.g. <c>.templates/meeting.md</c>);
/// <see cref="DisplayName"/> is the bare filename shown in the picker.
/// </summary>
public sealed record TemplateInfo(string RelativePath, string DisplayName);
