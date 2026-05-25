using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Platform.Storage;

namespace Notes.Services;

public sealed class AvaloniaFolderPicker : IFolderPicker
{
    public async Task<string?> PickFolder()
    {
        var window = (Application.Current as App)?.MainWindow;
        if (window is null)
        {
            return null;
        }

        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
        });

        var folder = folders.FirstOrDefault();
        return folder?.Path.LocalPath;
    }
}
