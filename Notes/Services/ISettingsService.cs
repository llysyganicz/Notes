using Notes.Models;

namespace Notes.Services;

public interface ISettingsService
{
    string ConfigFilePath { get; }
    AppSettings Load();
    void Save(AppSettings settings);
}
