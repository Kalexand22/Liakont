namespace Stratum.Common.UI.Tests.Unit;

using Bunit;
using FluentAssertions;
using Stratum.Common.UI.Components;
using Stratum.Common.UI.Models;
using Xunit;

public sealed class StratumDropdownTests : BunitContext
{
    public StratumDropdownTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void ShouldRenderWithoutData()
    {
        var cut = Render<StratumDropdown<string>>();

        cut.Find(".stratum-dropdown").Should().NotBeNull();
    }

    [Fact]
    public void ShouldRenderWithPlaceholder()
    {
        var cut = Render<StratumDropdown<string>>(p => p
            .Add(d => d.Placeholder, "Choose an option"));

        cut.Markup.Should().Contain("Choose an option");
    }

    [Fact]
    public void ShouldRenderWithData()
    {
        var items = new[] { "Alpha", "Beta", "Gamma" };

        var cut = Render<StratumDropdown<string>>(p => p
            .Add(d => d.Data, items));

        cut.Find(".stratum-dropdown").Should().NotBeNull();
    }

    [Fact]
    public void ShouldRenderWithObjectDataAndTextValueProperties()
    {
        var items = new[]
        {
            new StatusItem { Code = "A", Label = "Active" },
            new StatusItem { Code = "I", Label = "Inactive" },
        };

        var cut = Render<StratumDropdown<string>>(p => p
            .Add(d => d.Data, items)
            .Add(d => d.TextProperty, "Label")
            .Add(d => d.ValueProperty, "Code"));

        cut.Find(".stratum-dropdown").Should().NotBeNull();
    }

    [Fact]
    public void ShouldRenderDisabledState()
    {
        var cut = Render<StratumDropdown<string>>(p => p
            .Add(d => d.Disabled, true));

        cut.Find("[aria-disabled='true']").Should().NotBeNull();
    }

    [Fact]
    public void ShouldNotHaveErrorClassByDefault()
    {
        var cut = Render<StratumDropdown<string>>();

        cut.Find(".stratum-dropdown")
            .ClassList.Should().NotContain("stratum-dropdown--error");
    }

    [Fact]
    public void ShouldAddErrorClassWhenFormFieldContextHasError()
    {
        var context = new FormFieldContext { FieldId = "status-field", HasError = true };

        var cut = Render<StratumDropdown<string>>(p => p
            .AddCascadingValue(context));

        cut.Find(".stratum-dropdown")
            .ClassList.Should().Contain("stratum-dropdown--error");
    }

    [Fact]
    public void ShouldSetAriaInvalidWhenFormFieldContextHasError()
    {
        var context = new FormFieldContext { FieldId = "status-field", HasError = true };

        var cut = Render<StratumDropdown<string>>(p => p
            .AddCascadingValue(context));

        cut.Markup.Should().Contain("aria-invalid");
    }

    [Fact]
    public void ShouldSetAriaRequiredWhenFormFieldContextIsRequired()
    {
        var context = new FormFieldContext { FieldId = "status-field", Required = true };

        var cut = Render<StratumDropdown<string>>(p => p
            .AddCascadingValue(context));

        cut.Markup.Should().Contain("aria-required");
    }

    [Fact]
    public void ShouldSetAriaDescribedByFromFormFieldContext()
    {
        var context = new FormFieldContext { FieldId = "status-field", DescribedBy = "status-help" };

        var cut = Render<StratumDropdown<string>>(p => p
            .AddCascadingValue(context));

        cut.Markup.Should().Contain("aria-describedby");
        cut.Markup.Should().Contain("status-help");
    }

    [Fact]
    public void ShouldRenderWithPreselectedValue()
    {
        var items = new[] { "Alpha", "Beta", "Gamma" };

        var cut = Render<StratumDropdown<string>>(p => p
            .Add(d => d.Data, items)
            .Add(d => d.Value, "Beta"));

        cut.Find(".stratum-dropdown").Should().NotBeNull();
    }

    private sealed class StatusItem
    {
        public string Code { get; set; } = string.Empty;

        public string Label { get; set; } = string.Empty;
    }
}
