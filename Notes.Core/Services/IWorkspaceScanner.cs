using System.Collections.Generic;

namespace Notes.Core.Services;

public interface IWorkspaceScanner
{
    IReadOnlyList<string> ScanMarkdownFiles(string rootDirectory);
}
