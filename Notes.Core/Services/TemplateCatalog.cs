using System;
using System.Collections.Generic;
using Notes.Core.Models;

namespace Notes.Core.Services;

public sealed class TemplateCatalog : ITemplateCatalog
{
    private const string TemplatesPrefix = ".templates/";

    private IReadOnlyList<TemplateInfo> _templates = Array.Empty<TemplateInfo>();

    public void Load(IReadOnlyList<string> markdownRelativePaths)
    {
        var results = new List<TemplateInfo>();
        foreach (var relative in markdownRelativePaths)
        {
            if (IsTopLevelTemplate(relative, out var fileName))
            {
                results.Add(new TemplateInfo(relative, fileName));
            }
        }

        _templates = results;
    }

    public IReadOnlyList<TemplateInfo> List() => _templates;

    public bool HasAny() => _templates.Count > 0;

    private static bool IsTopLevelTemplate(string relativePath, out string fileName)
    {
        fileName = string.Empty;
        if (!relativePath.StartsWith(TemplatesPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var remainder = relativePath.Substring(TemplatesPrefix.Length);
        // Top level only — skip anything nested deeper than .templates/<file>.
        if (remainder.Length == 0 || remainder.Contains('/'))
        {
            return false;
        }

        fileName = remainder;
        return true;
    }
}
