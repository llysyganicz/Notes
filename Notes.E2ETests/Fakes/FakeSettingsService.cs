using Notes.Core.Models;
using Notes.Core.Services;

namespace Notes.E2ETests.Fakes;

public sealed class FakeSettingsService : ISettingsService
{
    private AppSettings _settings = AppSettings.Empty;

    public string ConfigFilePath => "/dev/null";

    public string? CurrentWorkspacePath => _settings.WorkspacePath;

    public AppSettings Load() => _settings;

    public void Save(AppSettings settings) => _settings = settings;
}
