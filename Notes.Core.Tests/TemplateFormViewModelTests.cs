using System;
using Notes.Core.Models;
using Notes.Core.ViewModels;
using Notes.Core.ViewModels.Fields;
using Xunit;

namespace Notes.Core.Tests;

public sealed class TemplateFormViewModelTests
{
    private static FormDefinition Definition(params FormFieldEntry[] fields) => new(fields);

    private static TemplateFormViewModel BuildSut(FormDefinition definition)
    {
        var vm = new TemplateFormViewModel();
        vm.Load(definition);
        return vm;
    }

    [Fact]
    public void Load_WhenDefinitionHasFields_BuildsOneVmPerFieldInDocumentOrder()
    {
        var definition = Definition(
            new FormFieldEntry("project", new FormField("text", "Project")),
            new FormFieldEntry("due", new FormField("date", "Due")),
            new FormFieldEntry("count", new FormField("number", "Count")));

        var sut = BuildSut(definition);

        Assert.Collection(
            sut.Fields,
            f => Assert.Equal("project", f.Name),
            f => Assert.Equal("due", f.Name),
            f => Assert.Equal("count", f.Name));
    }

    [Fact]
    public void Load_WhenFieldTypesVary_MapsEachToItsConcreteVm()
    {
        var definition = Definition(
            new FormFieldEntry("a", new FormField("text", "A")),
            new FormFieldEntry("b", new FormField("date", "B")),
            new FormFieldEntry("c", new FormField("number", "C")),
            new FormFieldEntry("d", new FormField("select", "D", new[] { "x", "y" })));

        var sut = BuildSut(definition);

        Assert.IsType<TextFieldVm>(sut.Fields[0]);
        Assert.IsType<DateFieldVm>(sut.Fields[1]);
        Assert.IsType<NumberFieldVm>(sut.Fields[2]);
        Assert.IsType<SelectFieldVm>(sut.Fields[3]);
    }

    [Fact]
    public void Load_WhenTypeCasingVaries_MatchesCaseInsensitively()
    {
        var definition = Definition(
            new FormFieldEntry("a", new FormField("DATE", "A")),
            new FormFieldEntry("b", new FormField("Select", "B", new[] { "x" })));

        var sut = BuildSut(definition);

        Assert.IsType<DateFieldVm>(sut.Fields[0]);
        Assert.IsType<SelectFieldVm>(sut.Fields[1]);
    }

    [Fact]
    public void Load_WhenTypeUnknown_FallsBackToTextField()
    {
        var definition = Definition(new FormFieldEntry("a", new FormField("mystery", "A")));

        var sut = BuildSut(definition);

        Assert.IsType<TextFieldVm>(sut.Fields[0]);
    }

    [Fact]
    public void Load_WhenSelect_PassesEntriesThrough()
    {
        var definition = Definition(
            new FormFieldEntry("p", new FormField("select", "P", new[] { "low", "high" })));

        var sut = BuildSut(definition);

        var select = Assert.IsType<SelectFieldVm>(sut.Fields[0]);
        Assert.Equal(new[] { "low", "high" }, select.Entries);
    }

    [Fact]
    public void Submit_WhenExecuted_YieldsNameToRenderValueMap()
    {
        var definition = Definition(
            new FormFieldEntry("project", new FormField("text", "Project")),
            new FormFieldEntry("due", new FormField("date", "Due")),
            new FormFieldEntry("count", new FormField("number", "Count")));
        var sut = BuildSut(definition);
        ((TextFieldVm)sut.Fields[0]).Value = "Apollo";
        ((DateFieldVm)sut.Fields[1]).Value = new DateTimeOffset(2026, 3, 7, 0, 0, 0, TimeSpan.Zero);
        ((NumberFieldVm)sut.Fields[2]).Value = 42m;

        sut.SubmitCommand.Execute(null);

        Assert.NotNull(sut.Result);
        Assert.Equal("Apollo", sut.Result!["project"]);
        Assert.Equal("2026-03-07", sut.Result["due"]);
        Assert.Equal("42", sut.Result["count"]);
    }

    [Fact]
    public void Submit_WhenFieldUntouched_YieldsEmptyString()
    {
        var definition = Definition(
            new FormFieldEntry("project", new FormField("text", "Project")),
            new FormFieldEntry("due", new FormField("date", "Due")));
        var sut = BuildSut(definition);

        sut.SubmitCommand.Execute(null);

        Assert.NotNull(sut.Result);
        Assert.Equal(string.Empty, sut.Result!["project"]);
        Assert.Equal(string.Empty, sut.Result["due"]);
    }

    [Fact]
    public void Submit_WhenDefinitionEmpty_YieldsEmptyMap()
    {
        var sut = BuildSut(FormDefinition.Empty);

        sut.SubmitCommand.Execute(null);

        Assert.NotNull(sut.Result);
        Assert.Empty(sut.Result!);
    }

    [Fact]
    public void Submit_WhenExecuted_RaisesCloseRequested()
    {
        var sut = BuildSut(FormDefinition.Empty);
        var closed = false;
        sut.CloseRequested += () => closed = true;

        sut.SubmitCommand.Execute(null);

        Assert.True(closed);
    }

    [Fact]
    public void Cancel_WhenExecuted_LeavesResultNullAndRaisesCloseRequested()
    {
        var sut = BuildSut(
            Definition(new FormFieldEntry("project", new FormField("text", "Project"))));
        var closed = false;
        sut.CloseRequested += () => closed = true;

        sut.CancelCommand.Execute(null);

        Assert.Null(sut.Result);
        Assert.True(closed);
    }

    // A second Load after a Submit must rebuild Fields from scratch and clear the prior Result.
    // Kills the removal of `Result = null;` (stale map would leak) and `Fields.Clear();` (the new
    // fields would be appended to the old ones).
    [Fact]
    public void Load_WhenCalledAfterSubmit_ReplacesFieldsAndResetsResult()
    {
        var sut = BuildSut(Definition(new FormFieldEntry("a", new FormField("text", "A"))));
        sut.SubmitCommand.Execute(null);
        Assert.NotNull(sut.Result);

        sut.Load(Definition(new FormFieldEntry("b", new FormField("text", "B"))));

        Assert.Null(sut.Result);
        var only = Assert.Single(sut.Fields);
        Assert.Equal("b", only.Name);
    }

    [Fact]
    public void Load_WhenSelectHasNoEntries_CreatesSelectVmWithEmptyChoicesAndEmptyRenderValue()
    {
        var definition = Definition(
            new FormFieldEntry("priority", new FormField("select", "Priority", Array.Empty<string>())));

        var sut = BuildSut(definition);

        var select = Assert.IsType<SelectFieldVm>(sut.Fields[0]);
        Assert.Empty(select.Entries);
        Assert.Equal(string.Empty, select.RenderValue());
    }
}
