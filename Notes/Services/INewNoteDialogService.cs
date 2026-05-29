using System;
using System.Threading.Tasks;

namespace Notes.Services;

public interface INewNoteDialogService
{
    Task<string?> PromptForName(string parentFolderDisplay, Func<string, string?> validate);
}
