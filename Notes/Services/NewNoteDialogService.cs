using System;
using System.Threading.Tasks;
using Avalonia;
using Notes.Views;

namespace Notes.Services;

public sealed class NewNoteDialogService : INewNoteDialogService
{
    public async Task<string?> PromptForName(string parentFolderDisplay, Func<string, string?> validate)
    {
        var owner = (Application.Current as App)?.MainWindow;
        if (owner is null)
        {
            return null;
        }

        return await NewNoteDialog.Show(owner, parentFolderDisplay, validate);
    }
}
