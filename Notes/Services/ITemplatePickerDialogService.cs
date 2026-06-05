using System.Collections.Generic;
using System.Threading.Tasks;
using Notes.Models;

namespace Notes.Services;

/// <summary>
/// Shows the template picker and returns the chosen <see cref="TemplateInfo"/>,
/// or <c>null</c> when the user cancels.
/// </summary>
public interface ITemplatePickerDialogService
{
    Task<TemplateInfo?> PickTemplate(IReadOnlyList<TemplateInfo> templates);
}
