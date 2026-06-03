namespace Stratum.Common.UI.Tests.Unit;

using FluentAssertions;
using Stratum.Common.Abstractions.Grid;
using Stratum.Common.UI.Services;
using Xunit;

public sealed class ViewModeRegistryTests
{
    [Fact]
    public void BuildWithNoViewsShouldThrow()
    {
        var builder = new ViewModeRegistryBuilder();

        var act = () => builder.Build();

        act.Should().Throw<ArgumentException>()
            .WithMessage("*At least one view mode*");
    }

    [Fact]
    public void DefaultViewShouldReturnFirstRegisteredKind()
    {
        var registry = new ViewModeRegistryBuilder()
            .AddCard()
            .AddTable()
            .Build();

        registry.DefaultView.Should().Be(ViewKind.Card);
    }

    [Fact]
    public void GetSupportedViewsShouldReturnAllInRegistrationOrder()
    {
        var registry = new ViewModeRegistryBuilder()
            .AddTable()
            .AddCard()
            .AddKanban("Status")
            .AddCalendar("DueDate")
            .Build();

        registry.GetSupportedViews().Should().HaveCount(4);
        registry.GetSupportedViews()[0].Kind.Should().Be(ViewKind.Table);
        registry.GetSupportedViews()[1].Kind.Should().Be(ViewKind.Card);
        registry.GetSupportedViews()[2].Kind.Should().Be(ViewKind.Kanban);
        registry.GetSupportedViews()[3].Kind.Should().Be(ViewKind.Calendar);
    }

    [Fact]
    public void GetDescriptorShouldReturnDescriptorForSupportedKind()
    {
        var registry = new ViewModeRegistryBuilder()
            .AddTable("Tableau", "bi-table")
            .Build();

        var descriptor = registry.GetDescriptor(ViewKind.Table);

        descriptor.Should().NotBeNull();
        descriptor!.Label.Should().Be("Tableau");
        descriptor.Icon.Should().Be("bi-table");
    }

    [Fact]
    public void GetDescriptorShouldReturnNullForUnsupportedKind()
    {
        var registry = new ViewModeRegistryBuilder()
            .AddTable()
            .Build();

        registry.GetDescriptor(ViewKind.Kanban).Should().BeNull();
    }

    [Fact]
    public void SupportsShouldReturnTrueForRegisteredKind()
    {
        var registry = new ViewModeRegistryBuilder()
            .AddTable()
            .AddCard()
            .Build();

        registry.Supports(ViewKind.Table).Should().BeTrue();
        registry.Supports(ViewKind.Card).Should().BeTrue();
    }

    [Fact]
    public void SupportsShouldReturnFalseForUnregisteredKind()
    {
        var registry = new ViewModeRegistryBuilder()
            .AddTable()
            .Build();

        registry.Supports(ViewKind.Calendar).Should().BeFalse();
    }

    [Fact]
    public void GetPropertyMappingShouldReturnMappingForKanban()
    {
        var registry = new ViewModeRegistryBuilder()
            .AddKanban("Status")
            .Build();

        registry.GetPropertyMapping(ViewKind.Kanban).Should().Be("Status");
    }

    [Fact]
    public void GetPropertyMappingShouldReturnMappingForCalendar()
    {
        var registry = new ViewModeRegistryBuilder()
            .AddCalendar("DueDate")
            .Build();

        registry.GetPropertyMapping(ViewKind.Calendar).Should().Be("DueDate");
    }

    [Fact]
    public void GetPropertyMappingShouldReturnNullForTable()
    {
        var registry = new ViewModeRegistryBuilder()
            .AddTable()
            .Build();

        registry.GetPropertyMapping(ViewKind.Table).Should().BeNull();
    }

    [Fact]
    public void GetPropertyMappingShouldReturnNullForUnregisteredKind()
    {
        var registry = new ViewModeRegistryBuilder()
            .AddTable()
            .Build();

        registry.GetPropertyMapping(ViewKind.Calendar).Should().BeNull();
    }

    [Fact]
    public void AddTableShouldUseDefaultLabelAndIcon()
    {
        var registry = new ViewModeRegistryBuilder()
            .AddTable()
            .Build();

        var descriptor = registry.GetDescriptor(ViewKind.Table);
        descriptor!.Label.Should().Be("Tableau");
        descriptor.Icon.Should().Be("bi-table");
    }

    [Fact]
    public void AddCardShouldUseDefaultLabelAndIcon()
    {
        var registry = new ViewModeRegistryBuilder()
            .AddCard()
            .Build();

        var descriptor = registry.GetDescriptor(ViewKind.Card);
        descriptor!.Label.Should().Be("Cartes");
        descriptor.Icon.Should().Be("bi-grid-3x3-gap");
    }

    [Fact]
    public void KanbanDescriptorShouldHaveRequiredPropertyHint()
    {
        var registry = new ViewModeRegistryBuilder()
            .AddKanban("Status")
            .Build();

        var descriptor = registry.GetDescriptor(ViewKind.Kanban);
        descriptor!.RequiredPropertyHint.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CalendarDescriptorShouldHaveRequiredPropertyHint()
    {
        var registry = new ViewModeRegistryBuilder()
            .AddCalendar("DueDate")
            .Build();

        var descriptor = registry.GetDescriptor(ViewKind.Calendar);
        descriptor!.RequiredPropertyHint.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void AddCustomDescriptorShouldWork()
    {
        var custom = new ViewModeDescriptor(ViewKind.Table, "Custom Table", "bi-custom");
        var registry = new ViewModeRegistryBuilder()
            .Add(custom, "SomeProperty")
            .Build();

        registry.Supports(ViewKind.Table).Should().BeTrue();
        registry.GetDescriptor(ViewKind.Table)!.Label.Should().Be("Custom Table");
        registry.GetPropertyMapping(ViewKind.Table).Should().Be("SomeProperty");
    }

    [Fact]
    public void DuplicateRegistrationShouldThrow()
    {
        var builder = new ViewModeRegistryBuilder()
            .AddTable();

        var act = () => builder.AddTable();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Table*already registered*");
    }

    [Fact]
    public void FullRegistryWithAllFourViewsShouldWork()
    {
        var registry = new ViewModeRegistryBuilder()
            .AddTable()
            .AddCard()
            .AddKanban("Status")
            .AddCalendar("DueDate")
            .Build();

        registry.GetSupportedViews().Should().HaveCount(4);
        registry.DefaultView.Should().Be(ViewKind.Table);
        registry.Supports(ViewKind.Table).Should().BeTrue();
        registry.Supports(ViewKind.Card).Should().BeTrue();
        registry.Supports(ViewKind.Kanban).Should().BeTrue();
        registry.Supports(ViewKind.Calendar).Should().BeTrue();
        registry.GetPropertyMapping(ViewKind.Kanban).Should().Be("Status");
        registry.GetPropertyMapping(ViewKind.Calendar).Should().Be("DueDate");
    }
}
