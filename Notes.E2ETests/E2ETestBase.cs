using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Notes.Core.Models;
using Notes.Core.Services;
using Notes.E2ETests.Fakes;
using Notes.Core.ViewModels;
using Notes.Services;
using Notes.ViewModels;
using Notes.Views;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using TextEditor = AvaloniaEdit.TextEditor;
using Xunit;

namespace Notes.E2ETests;

public abstract class E2ETestBase : IAsyncLifetime
{
    protected string WorkspacePath { get; private set; } = null!;
    protected MockFileSystem FileSystem { get; private set; } = null!;
    protected ServiceProvider Services { get; private set; } = null!;
    protected MainWindow MainWindow { get; private set; } = null!;
    protected StrongReferenceMessenger Messenger { get; private set; } = null!;
    protected FakeFolderPicker FolderPicker { get; private set; } = null!;
    protected FakeSettingsService SettingsService { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        WorkspacePath = $"/test-workspace-{Guid.NewGuid()}";
        FileSystem = new MockFileSystem();
        FileSystem.AddDirectory(WorkspacePath);
        Messenger = new StrongReferenceMessenger();

        FolderPicker = new FakeFolderPicker { Result = WorkspacePath };
        SettingsService = new FakeSettingsService();

        Services = BuildServiceProvider();
        App.Services = Services;

        MainWindow = Services.GetRequiredService<MainWindow>();
        MainWindow.Show();

        if (Application.Current is App app)
        {
            app.MainWindow = MainWindow;
        }

        var mainViewModel = Services.GetRequiredService<MainWindowViewModel>();
        var ready = await mainViewModel.InitializeAsync();
        if (!ready)
        {
            throw new InvalidOperationException("Workspace initialization failed.");
        }
    }

    public ValueTask DisposeAsync()
    {
        Services.GetRequiredService<IAutoSaveScheduler>().Cancel();
        MainWindow.Close();
        Services.Dispose();
        return ValueTask.CompletedTask;
    }

    protected T FindControl<T>(string? name = null) where T : Control
    {
        if (name is not null)
        {
            var direct = MainWindow.FindControl<T>(name);
            if (direct is not null)
            {
                return direct;
            }

            var found = MainWindow.GetVisualDescendants()
                .OfType<T>()
                .FirstOrDefault(c => c.Name == name);

            return found ?? throw new InvalidOperationException(
                $"Control '{name}' of type {typeof(T).Name} was not found.");
        }

        return MainWindow.GetVisualDescendants().OfType<T>().FirstOrDefault()
            ?? throw new InvalidOperationException($"No control of type {typeof(T).Name} was found.");
    }

    protected Task ClickButtonAsync(string name)
    {
        var button = FindControl<Button>(name);
        if (button.Command is not null && button.Command.CanExecute(button.CommandParameter))
        {
            button.Command.Execute(button.CommandParameter);
        }
        else
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }

        return Task.CompletedTask;
    }

    protected Task SetTextBoxTextAsync(string name, string text)
    {
        var textBox = FindControl<TextBox>(name);
        textBox.Text = text;
        return Task.CompletedTask;
    }

    protected Task SelectTreeItemAsync(string headerText)
    {
        var treeViewModel = Services.GetRequiredService<NoteTreeViewModel>();
        var match = FindNode(treeViewModel.Root, headerText);
        if (match is null)
        {
            throw new InvalidOperationException($"Tree item '{headerText}' was not found.");
        }

        treeViewModel.SelectedNode = match;
        return Task.CompletedTask;
    }

    protected string GetEditorText()
    {
        var editor = MainWindow.GetVisualDescendants().OfType<TextEditor>().FirstOrDefault()
            ?? throw new InvalidOperationException("TextEditor was not found.");
        return editor.Text;
    }

    protected void FlushAutoSave() => Services.GetRequiredService<IAutoSaveScheduler>().Flush();

    private static NoteTreeNode? FindNode(NoteTreeNode? node, string name)
    {
        if (node is null)
        {
            return null;
        }

        if (node.Name == name)
        {
            return node;
        }

        return node.Children.Select(child => FindNode(child, name)).FirstOrDefault(found => found is not null);
    }

    private ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IMessenger>(Messenger);
        services.AddSingleton<IFileSystem>(FileSystem);
        services.AddSingleton<ISettingsService>(SettingsService);
        services.AddSingleton<IPathGuard, PathGuard>();
        services.AddSingleton<IWorkspaceScanner, WorkspaceScanner>();
        services.AddSingleton<NoteTreeBuilder>();
        services.AddSingleton<INoteDeleter, NoteDeleter>();
        services.AddSingleton<IFolderPicker>(FolderPicker);
        services.AddSingleton<IConfirmDialogService, ConfirmDialogService>();
        services.AddSingleton<INoteFileService, NoteFileService>();
        services.AddSingleton<INoteMetadataParser, NoteMetadataParser>();
        services.AddSingleton<INoteSearchIndex, NoteSearchIndex>();
        services.AddSingleton<IAutoSaveScheduler, AutoSaveScheduler>();
        services.AddSingleton<INameValidator, NameValidator>();
        services.AddSingleton<INoteFolderService, NoteFolderService>();
        services.AddSingleton<INewNoteDialogService, NewNoteDialogService>();
        services.AddSingleton<ITemplateParser, TemplateParser>();
        services.AddSingleton<ITemplateRenderer, TemplateRenderer>();
        services.AddSingleton<ITemplateCatalog, TemplateCatalog>();
        services.AddSingleton<ITemplatePickerDialogService, TemplatePickerDialogService>();
        services.AddSingleton<ITemplateFormDialogService, TemplateFormDialogService>();
        services.AddSingleton<OrphanedTempCleaner>();

        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<NoteTreeViewModel>();
        services.AddSingleton<NoteEditorViewModel>();
        services.AddSingleton<NoteSearchViewModel>();

        services.AddTransient<MainWindow>();
        services.AddTransient<TemplateFormViewModel>();
        services.AddTransient<TemplateFormDialog>();
        services.AddTransient<TemplatePickerViewModel>();
        services.AddTransient<TemplatePickerDialog>();

        return services.BuildServiceProvider();
    }
}
