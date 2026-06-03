namespace Stratum.Common.Abstractions.Tests.Unit.Grid;

using FluentAssertions;
using Stratum.Common.Abstractions.Grid;
using Xunit;

public sealed class ViewModeDescriptorTests
{
    [Fact]
    public void DescriptorShouldStoreAllProperties()
    {
        var descriptor = new ViewModeDescriptor(
            ViewKind.Kanban,
            "Kanban",
            "bi-kanban",
            RequiredPropertyHint: "GroupBy property");

        descriptor.Kind.Should().Be(ViewKind.Kanban);
        descriptor.Label.Should().Be("Kanban");
        descriptor.Icon.Should().Be("bi-kanban");
        descriptor.RequiredPropertyHint.Should().Be("GroupBy property");
    }

    [Fact]
    public void RequiredPropertyHintShouldDefaultToNull()
    {
        var descriptor = new ViewModeDescriptor(ViewKind.Table, "Tableau", "bi-table");

        descriptor.RequiredPropertyHint.Should().BeNull();
    }

    [Fact]
    public void ViewKindEnumShouldContainExpectedMembers()
    {
        Enum.GetValues<ViewKind>().Should().Contain(new[]
        {
            ViewKind.Table,
            ViewKind.Card,
            ViewKind.Kanban,
            ViewKind.Calendar,
        });
    }

    [Theory]
    [InlineData(ViewKind.Table)]
    [InlineData(ViewKind.Card)]
    [InlineData(ViewKind.Kanban)]
    [InlineData(ViewKind.Calendar)]
    public void ViewKindShouldContainExpectedValue(ViewKind kind)
    {
        Enum.IsDefined(kind).Should().BeTrue();
    }

    [Fact]
    public void DescriptorEqualityShouldBeValueBased()
    {
        var a = new ViewModeDescriptor(ViewKind.Card, "Cartes", "bi-grid");
        var b = new ViewModeDescriptor(ViewKind.Card, "Cartes", "bi-grid");

        a.Should().Be(b);
    }

    [Fact]
    public void DescriptorsShouldNotBeEqualWhenKindDiffers()
    {
        var a = new ViewModeDescriptor(ViewKind.Card, "Cartes", "bi-grid");
        var b = new ViewModeDescriptor(ViewKind.Table, "Cartes", "bi-grid");

        a.Should().NotBe(b);
    }
}
