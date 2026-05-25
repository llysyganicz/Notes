using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Notes.Models;
using Notes.Services;

namespace Notes.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IFolderPicker _folderPicker;

    [ObservableProperty]
    private string? _workspacePath;

    public MainWindowViewModel(ISettingsService settingsService, IFolderPicker folderPicker)
    {
        _settingsService = settingsService;
        _folderPicker = folderPicker;
    }

    [RelayCommand]
    private async Task ChangeWorkspace()
    {
        var picked = await _folderPicker.PickFolder();
        if (picked is null)
        {
            return;
        }

        _settingsService.Save(new AppSettings(picked));
        WorkspacePath = picked;
    }

    [RelayCommand]
    private void Exit()
    {
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }
}
