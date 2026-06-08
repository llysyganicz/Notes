using System;
using System.Diagnostics;
using System.IO.Abstractions;
using CommunityToolkit.Mvvm.Messaging;
using Notes.Messaging;

namespace Notes.Services;

public sealed class OrphanedTempCleaner :
    IRecipient<WorkspaceChangedMessage>
{
    private readonly IFileSystem _fileSystem;
    private readonly IMessenger _messenger;

    public OrphanedTempCleaner(IFileSystem fileSystem, IMessenger messenger)
    {
        _fileSystem = fileSystem;
        _messenger = messenger;
        _messenger.RegisterAll(this);
    }

    public void Receive(WorkspaceChangedMessage message)
    {
        var root = message.WorkspacePath;
        if (string.IsNullOrEmpty(root) || !_fileSystem.Directory.Exists(root))
            return;

        foreach (var tmpFile in _fileSystem.Directory.GetFiles(root, "*" + NoteFileService.TempSuffix, System.IO.SearchOption.AllDirectories))
        {
            try { _fileSystem.File.Delete(tmpFile); }
            catch (Exception ex) { Trace.WriteLine($"OrphanedTempCleaner: could not delete '{tmpFile}': {ex.Message}"); }
        }
    }
}
