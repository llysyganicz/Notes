using Notes.Core.Models;

namespace Notes.Core.Services;

public interface ISettingsService
{
    string ConfigFilePath { get; }
    string? CurrentWorkspacePath { get; }
    AppSettings Load();
    void Save(AppSettings settings);
}
