using System;
using Notes.Models;
using Notes.ViewModels;
using Xunit;

namespace Notes.Tests;

public sealed class TemplatePickerViewModelTests
{
    private static TemplateInfo Template(string name) => new($".templates/{name}", name);

    [Fact]
    public void Load_WhenTemplatesProvided_PopulatesAndSelectsFirst()
    {
        var sut = new TemplatePickerViewModel();

        sut.Load(new[] { Template("a.md"), Template("b.md") });

        Assert.Collection(
            sut.Templates,
            t => Assert.Equal("a.md", t.DisplayName),
            t => Assert.Equal("b.md", t.DisplayName));
        Assert.Equal("a.md", sut.SelectedTemplate?.DisplayName);
        Assert.True(sut.SubmitCommand.CanExecute(null));
    }

    [Fact]
    public void Load_WhenEmpty_LeavesNoSelectionAndDisablesSubmit()
    {
        var sut = new TemplatePickerViewModel();

        sut.Load(Array.Empty<TemplateInfo>());

        Assert.Empty(sut.Templates);
        Assert.Null(sut.SelectedTemplate);
        Assert.False(sut.SubmitCommand.CanExecute(null));
    }

    [Fact]
    public void Submit_WhenExecuted_SetsResultToSelectionAndRaisesCloseRequested()
    {
        var sut = new TemplatePickerViewModel();
        sut.Load(new[] { Template("a.md"), Template("b.md") });
        sut.SelectedTemplate = sut.Templates[1];
        var closed = false;
        sut.CloseRequested += () => closed = true;

        sut.SubmitCommand.Execute(null);

        Assert.Equal("b.md", sut.Result?.DisplayName);
        Assert.True(closed);
    }

    [Fact]
    public void Cancel_WhenExecuted_LeavesResultNullAndRaisesCloseRequested()
    {
        var sut = new TemplatePickerViewModel();
        sut.Load(new[] { Template("a.md") });
        var closed = false;
        sut.CloseRequested += () => closed = true;

        sut.CancelCommand.Execute(null);

        Assert.Null(sut.Result);
        Assert.True(closed);
    }

    [Fact]
    public void Load_WhenReloaded_ResetsPreviousResult()
    {
        var sut = new TemplatePickerViewModel();
        sut.Load(new[] { Template("a.md") });
        sut.SubmitCommand.Execute(null);
        Assert.NotNull(sut.Result);

        sut.Load(new[] { Template("b.md") });

        Assert.Null(sut.Result);
    }
}
