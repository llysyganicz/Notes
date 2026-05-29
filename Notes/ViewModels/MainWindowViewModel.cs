using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Notes.Messaging;
using Notes.Models;
using Notes.Services;

namespace Notes.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IMessenger _messenger;
    private readonly ISettingsService _settingsService;
    private readonly IFolderPicker _folderPicker;

    [ObservableProperty]
    private string? _workspacePath;

    public MainWindowViewModel(
        IMessenger messenger,
        ISettingsService settingsService,
        IFolderPicker folderPicker)
    {
        _messenger = messenger;
        _settingsService = settingsService;
        _folderPicker = folderPicker;
    }

    public async Task<bool> InitializeAsync()
    {
        var settings = _settingsService.Load();
        if (!string.IsNullOrEmpty(settings.WorkspacePath) && !Directory.Exists(settings.WorkspacePath))
        {
            _settingsService.Save(AppSettings.Empty);
            settings = AppSettings.Empty;
        }

        string workspace;
        if (string.IsNullOrEmpty(settings.WorkspacePath))
        {
            var picked = await _folderPicker.PickFolder();
            if (picked is null)
            {
                return false;
            }

            _settingsService.Save(new AppSettings(picked));
            workspace = picked;
        }
        else
        {
            workspace = settings.WorkspacePath;
        }

        WorkspacePath = workspace;
        _messenger.Send(new WorkspaceChangedMessage(workspace));
        return true;
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
        _messenger.Send(new WorkspaceChangedMessage(picked));
    }

    [RelayCommand]
    private void NewNote()
    {
        _messenger.Send(new NewNoteRequestedMessage());
    }

    [RelayCommand]
    private void TogglePreview()
    {
        _messenger.Send(new TogglePreviewRequestedMessage());
    }

    [RelayCommand]
    private void Exit()
    {
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }
}
