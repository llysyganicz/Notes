using System.Collections.Generic;

namespace Notes.Services;

public interface IWorkspaceScanner
{
    IReadOnlyList<string> ScanMarkdownFiles(string rootDirectory);
}
