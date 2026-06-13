using System.Collections.Generic;
using System.Threading.Tasks;
using Notes.Core.Models;

namespace Notes.Services;

/// <summary>
/// Shows the dynamic typed form for a template's <see cref="FormDefinition"/> and returns
/// the collected <c>name → formatted-string</c> map, or <c>null</c> when the user cancels.
/// </summary>
public interface ITemplateFormDialogService
{
    Task<IReadOnlyDictionary<string, string>?> CollectValues(FormDefinition definition);
}
