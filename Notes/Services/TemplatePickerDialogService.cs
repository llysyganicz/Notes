using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Notes.Core.Models;
using Notes.ViewModels;
using Notes.Core.ViewModels;
using Notes.Views;

namespace Notes.Services;

public sealed class TemplatePickerDialogService : ITemplatePickerDialogService
{
    public async Task<TemplateInfo?> PickTemplate(IReadOnlyList<TemplateInfo> templates)
    {
        var owner = (Application.Current as App)?.MainWindow;
        if (owner is null)
        {
            return null;
        }

        // The dialog's DataContext is the locator-resolved (transient) picker VM.
        var dialog = App.Services.GetRequiredService<TemplatePickerDialog>();
        var viewModel = (TemplatePickerViewModel)dialog.DataContext!;
        viewModel.Load(templates);

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
