using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Notes.Core.Models;
using Notes.ViewModels;
using Notes.Core.ViewModels;
using Notes.Views;

namespace Notes.Services;

public sealed class TemplateFormDialogService : ITemplateFormDialogService
{
    public async Task<IReadOnlyDictionary<string, string>?> CollectValues(FormDefinition definition)
    {
        var owner = (Application.Current as App)?.MainWindow;
        if (owner is null)
        {
            return null;
        }

        // The dialog's DataContext is the locator-resolved (transient) form VM.
        var dialog = App.Services.GetRequiredService<TemplateFormDialog>();
        var viewModel = (TemplateFormViewModel)dialog.DataContext!;
        viewModel.Load(definition);

        void OnCloseRequested() => dialog.Close();
        viewModel.CloseRequested += OnCloseRequested;
        try
        {
            await dialog.ShowDialog(owner);
        }
        finally
        {
            viewModel.CloseRequested -= OnCloseRequested;
        }

        return viewModel.Result;
    }
}
