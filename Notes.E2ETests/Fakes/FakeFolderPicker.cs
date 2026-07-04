using System.Threading.Tasks;
using Notes.Services;

namespace Notes.E2ETests.Fakes;

public sealed class FakeFolderPicker : IFolderPicker
{
    public string? Result { get; set; }

    public Task<string?> PickFolder() => Task.FromResult(Result);
}
