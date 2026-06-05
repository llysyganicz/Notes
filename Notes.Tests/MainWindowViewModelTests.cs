using CommunityToolkit.Mvvm.Messaging;
using Notes.Messaging;
using Notes.Services;
using Notes.ViewModels;
using NSubstitute;
using Xunit;

namespace Notes.Tests;

public sealed class MainWindowViewModelTests
{
    private readonly StrongReferenceMessenger _messenger = new();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly IFolderPicker _folderPicker = Substitute.For<IFolderPicker>();
    private readonly ITemplateCatalog _templateCatalog = Substitute.For<ITemplateCatalog>();

    private MainWindowViewModel BuildSut() =>
        new(_messenger, _settings, _folderPicker, _templateCatalog);

    [Fact]
    public void NewFromTemplateCommand_WhenNoTemplates_CannotExecute()
    {
        var sut = BuildSut();

        Assert.False(sut.HasTemplates);
        Assert.False(sut.NewFromTemplateCommand.CanExecute(null));
    }

    [Fact]
    public void Receive_WhenTemplatesChangedAndCatalogHasAny_EnablesCommand()
    {
        _templateCatalog.HasAny().Returns(true);
        var sut = BuildSut();

        _messenger.Send(new TemplatesChangedMessage());

        Assert.True(sut.HasTemplates);
        Assert.True(sut.NewFromTemplateCommand.CanExecute(null));
    }

    [Fact]
    public void Receive_WhenTemplatesChangedAndCatalogEmpty_DisablesCommand()
    {
        _templateCatalog.HasAny().Returns(true);
        var sut = BuildSut();
        _messenger.Send(new TemplatesChangedMessage());
        Assert.True(sut.HasTemplates);

        _templateCatalog.HasAny().Returns(false);
        _messenger.Send(new TemplatesChangedMessage());

        Assert.False(sut.HasTemplates);
        Assert.False(sut.NewFromTemplateCommand.CanExecute(null));
    }

    [Fact]
    public void NewFromTemplateCommand_WhenExecuted_SendsRequestMessage()
    {
        _templateCatalog.HasAny().Returns(true);
        var sut = BuildSut();
        _messenger.Send(new TemplatesChangedMessage());

        var received = false;
        _messenger.Register<NewFromTemplateRequestedMessage>(this, (_, _) => received = true);

        sut.NewFromTemplateCommand.Execute(null);

        Assert.True(received);
    }
}
