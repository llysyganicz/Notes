using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Notes.Core.Models;
using Notes.Core.Services;

namespace Notes.Services;

/// <summary>
/// Encapsulates the shared template orchestration used by both the "New from Template"
/// menu path and the editor "Insert from Template" path.
/// </summary>
public sealed class TemplateService : ITemplateService
{
    private readonly ITemplateCatalog _templateCatalog;
    private readonly ITemplatePickerDialogService _templatePickerDialog;
    private readonly INoteFileService _fileService;
    private readonly ITemplateParser _templateParser;
    private readonly ITemplateFormDialogService _templateFormDialog;
    private readonly ITemplateRenderer _templateRenderer;

    public TemplateService(
        ITemplateCatalog templateCatalog,
        ITemplatePickerDialogService templatePickerDialog,
        INoteFileService fileService,
        ITemplateParser templateParser,
        ITemplateFormDialogService templateFormDialog,
        ITemplateRenderer templateRenderer)
    {
        _templateCatalog = templateCatalog;
        _templatePickerDialog = templatePickerDialog;
        _fileService = fileService;
        _templateParser = templateParser;
        _templateFormDialog = templateFormDialog;
        _templateRenderer = templateRenderer;
    }

    public async Task<string?> RenderForNewNote(string workspacePath)
    {
        var inputs = await CollectRenderInputs(workspacePath);
        if (inputs.TemplateText is null)
        {
            return null;
        }

        return _templateRenderer.Render(inputs.TemplateText, inputs.Definition, inputs.Values);
    }

    public async Task<string?> RenderForInsert(string workspacePath)
    {
        var inputs = await CollectRenderInputs(workspacePath);
        if (inputs.TemplateText is null)
        {
            return null;
        }

        return _templateRenderer.RenderBody(inputs.TemplateText, inputs.Definition, inputs.Values);
    }

    private async Task<CollectedTemplateInputs> CollectRenderInputs(string workspacePath)
    {
        var templates = _templateCatalog.List();
        if (templates.Count == 0)
        {
            return new CollectedTemplateInputs(null, FormDefinition.Empty, new Dictionary<string, string>());
        }

        var picked = await _templatePickerDialog.PickTemplate(templates);
        if (picked is null)
        {
            return new CollectedTemplateInputs(null, FormDefinition.Empty, new Dictionary<string, string>());
        }

        var templateAbsolute = Path.Combine(
            workspacePath,
            picked.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var templateText = _fileService.Read(templateAbsolute);

        var definition = _templateParser.Parse(templateText);

        IReadOnlyDictionary<string, string> values;
        if (definition.Fields.Count > 0)
        {
            var collected = await _templateFormDialog.CollectValues(definition);
            if (collected is null)
            {
                return new CollectedTemplateInputs(null, FormDefinition.Empty, new Dictionary<string, string>());
            }

            values = collected;
        }
        else
        {
            values = new Dictionary<string, string>();
        }

        return new CollectedTemplateInputs(templateText, definition, values);
    }

    private sealed record CollectedTemplateInputs(
        string? TemplateText,
        FormDefinition Definition,
        IReadOnlyDictionary<string, string> Values);
}
