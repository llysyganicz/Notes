using Notes.Models;

namespace Notes.Services;

public interface ISettingsService
{
    string ConfigFilePath { get; }
    string? CurrentWorkspacePath { get; }
    AppSettings Load();
    void Save(AppSettings settings);
}
