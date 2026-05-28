using System.Threading.Tasks;
using Avalonia;
using Notes.Views;

namespace Notes.Services;

public sealed class ConfirmDialogService : IConfirmDialogService
{
    public async Task<bool> Confirm(string title, string message)
    {
        var owner = (Application.Current as App)?.MainWindow;
        if (owner is null)
        {
            return false;
        }

        return await ConfirmDialog.Show(owner, title, message);
    }
}
