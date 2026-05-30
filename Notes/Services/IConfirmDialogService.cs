using System.Threading.Tasks;

namespace Notes.Services;

public interface IConfirmDialogService
{
    Task<bool> Confirm(string title, string message);
}
