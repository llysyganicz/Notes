using System.Threading.Tasks;

namespace Notes.Services;

public interface IFolderPicker
{
    Task<string?> PickFolder();
}
