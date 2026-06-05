using System;
using System.Globalization;
using Notes.ViewModels.Fields;
using Xunit;

namespace Notes.Tests;

public sealed class FieldVmTests
{
    [Fact]
    public void RenderValue_WhenTextEntered_ReturnsVerbatim()
    {
        var sut = new TextFieldVm("title", "Title") { Value = "Hello World" };

        Assert.Equal("Hello World", sut.RenderValue());
    }

    [Fact]
    public void RenderValue_WhenTextEmpty_ReturnsEmpty()
    {
        var sut = new TextFieldVm("title", "Title");

        Assert.Equal(string.Empty, sut.RenderValue());
    }

    [Fact]
    public void RenderValue_WhenSelectUnselected_ReturnsEmpty()
    {
        var sut = new SelectFieldVm("priority", "Priority", new[] { "low", "high" });

        Assert.Equal(string.Empty, sut.RenderValue());
    }

    [Fact]
    public void RenderValue_WhenSelectChosen_ReturnsEntry()
    {
        var sut = new SelectFieldVm("priority", "Priority", new[] { "low", "high" })
        {
            SelectedEntry = "high",
        };

        Assert.Equal("high", sut.RenderValue());
    }

    [Fact]
    public void RenderValue_WhenNumberEmpty_ReturnsEmpty()
    {
        var sut = new NumberFieldVm("count", "Count");

        Assert.Equal(string.Empty, sut.RenderValue());
    }

    [Fact]
    public void RenderValue_WhenNumberWithoutFormat_ReturnsInvariantRoundTrip()
    {
        var sut = new NumberFieldVm("count", "Count") { Value = 1234.5m };

        Assert.Equal("1234.5", sut.RenderValue());
    }

    [Fact]
    public void RenderValue_WhenNumberFormatSet_AppliesFormat()
    {
        var sut = new NumberFieldVm("price", "Price", "F2") { Value = 3m };

        Assert.Equal("3.00", sut.RenderValue());
    }

    [Fact]
    public void RenderValue_WhenNumberInCommaDecimalCulture_StaysInvariant()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            // de-DE uses a comma as the decimal separator; the rendered value must not.
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var sut = new NumberFieldVm("price", "Price", "F2") { Value = 3.5m };

            Assert.Equal("3.50", sut.RenderValue());
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void ParsingNumberStyle_WhenIntegerFormat_RejectsDecimals()
    {
        Assert.Equal(NumberStyles.Integer, new NumberFieldVm("n", "N", "0").ParsingNumberStyle);
        Assert.Equal(NumberStyles.Integer, new NumberFieldVm("n", "N", "F0").ParsingNumberStyle);
    }

    [Fact]
    public void ParsingNumberStyle_WhenDecimalOrAbsentFormat_AllowsDecimals()
    {
        Assert.Equal(NumberStyles.Number, new NumberFieldVm("n", "N", "F2").ParsingNumberStyle);
        Assert.Equal(NumberStyles.Number, new NumberFieldVm("n", "N", "0.##").ParsingNumberStyle);
        Assert.Equal(NumberStyles.Number, new NumberFieldVm("n", "N").ParsingNumberStyle);
    }

    [Fact]
    public void RenderValue_WhenDateEmpty_ReturnsEmpty()
    {
        var sut = new DateFieldVm("due", "Due");

        Assert.Equal(string.Empty, sut.RenderValue());
    }

    [Fact]
    public void RenderValue_WhenDateWithoutFormat_ReturnsIso()
    {
        var sut = new DateFieldVm("due", "Due")
        {
            Value = new DateTimeOffset(2026, 3, 7, 0, 0, 0, TimeSpan.Zero),
        };

        Assert.Equal("2026-03-07", sut.RenderValue());
    }

    [Fact]
    public void RenderValue_WhenDateFormatSet_AppliesFormat()
    {
        var sut = new DateFieldVm("due", "Due", "dd/MM/yyyy")
        {
            Value = new DateTimeOffset(2026, 3, 7, 0, 0, 0, TimeSpan.Zero),
        };

        Assert.Equal("07/03/2026", sut.RenderValue());
    }
}
