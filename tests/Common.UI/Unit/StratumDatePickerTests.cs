namespace Stratum.Common.UI.Tests.Unit;

using Bunit;
using FluentAssertions;
using Stratum.Common.UI.Components;
using Stratum.Common.UI.Models;
using Xunit;

public sealed class StratumDatePickerTests : BunitContext
{
    public StratumDatePickerTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void ShouldRenderWithDefaultPlaceholder()
    {
        var cut = Render<StratumDatePicker>();

        cut.Find(".stratum-datepicker").Should().NotBeNull();
    }

    [Fact]
    public void ShouldRenderWithCustomPlaceholder()
    {
        var cut = Render<StratumDatePicker>(p => p
            .Add(d => d.Placeholder, "Select a date"));

        cut.Markup.Should().Contain("Select a date");
    }

    [Fact]
    public void ShouldNotHaveErrorClassByDefault()
    {
        var cut = Render<StratumDatePicker>();

        cut.Find(".stratum-datepicker")
            .ClassList.Should().NotContain("stratum-datepicker--error");
    }

    [Fact]
    public void ShouldAddErrorClassWhenFormFieldContextHasError()
    {
        var context = new FormFieldContext { FieldId = "date-field", HasError = true };

        var cut = Render<StratumDatePicker>(p => p
            .AddCascadingValue(context));

        cut.Find(".stratum-datepicker")
            .ClassList.Should().Contain("stratum-datepicker--error");
    }

    [Fact]
    public void ShouldSetAriaInvalidWhenFormFieldContextHasError()
    {
        var context = new FormFieldContext { FieldId = "date-field", HasError = true };

        var cut = Render<StratumDatePicker>(p => p
            .AddCascadingValue(context));

        cut.Markup.Should().Contain("aria-invalid");
    }

    [Fact]
    public void ShouldSetAriaRequiredWhenFormFieldContextIsRequired()
    {
        var context = new FormFieldContext { FieldId = "date-field", Required = true };

        var cut = Render<StratumDatePicker>(p => p
            .AddCascadingValue(context));

        cut.Markup.Should().Contain("aria-required");
    }

    [Fact]
    public void ShouldSetAriaDescribedByFromFormFieldContext()
    {
        var context = new FormFieldContext { FieldId = "date-field", DescribedBy = "date-help-text" };

        var cut = Render<StratumDatePicker>(p => p
            .AddCascadingValue(context));

        cut.Markup.Should().Contain("aria-describedby");
        cut.Markup.Should().Contain("date-help-text");
    }

    [Fact]
    public void ShouldRenderDisabledState()
    {
        var cut = Render<StratumDatePicker>(p => p
            .Add(d => d.Disabled, true));

        cut.Find("[disabled]").Should().NotBeNull();
    }

    [Fact]
    public void ShouldRenderWithMinMaxDates()
    {
        var min = new DateOnly(2025, 1, 1);
        var max = new DateOnly(2025, 12, 31);

        var cut = Render<StratumDatePicker>(p => p
            .Add(d => d.Min, min)
            .Add(d => d.Max, max));

        cut.Find(".stratum-datepicker").Should().NotBeNull();
    }

    [Fact]
    public void ShouldRenderWithPreselectedValue()
    {
        var date = new DateOnly(2025, 6, 15);

        var cut = Render<StratumDatePicker>(p => p
            .Add(d => d.Value, date));

        cut.Find(".stratum-datepicker").Should().NotBeNull();
    }
}
