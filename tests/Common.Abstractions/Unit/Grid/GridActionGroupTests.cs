namespace Stratum.Common.Abstractions.Tests.Unit.Grid;

using FluentAssertions;
using Stratum.Common.Abstractions.Grid;
using Xunit;

public sealed class GridActionGroupTests
{
    [Fact]
    public void ShouldStoreAllProperties()
    {
        var actions = new List<GridAction>
        {
            new("dup", "Dupliquer"),
            new("status", "Changer statut"),
        };

        var group = new GridActionGroup("Actions", "bi-gear", actions);

        group.Label.Should().Be("Actions");
        group.Icon.Should().Be("bi-gear");
        group.Actions.Should().HaveCount(2);
        group.Actions[0].Id.Should().Be("dup");
        group.Actions[1].Id.Should().Be("status");
    }

    [Fact]
    public void IconCanBeNull()
    {
        var group = new GridActionGroup("Menu", null, Array.Empty<GridAction>());

        group.Icon.Should().BeNull();
    }

    [Fact]
    public void EmptyActionsListShouldBeAllowed()
    {
        var group = new GridActionGroup("Vide", null, Array.Empty<GridAction>());

        group.Actions.Should().BeEmpty();
    }

    [Fact]
    public void RecordEqualityShouldBeValueBased()
    {
        var actions = new List<GridAction> { new("a", "A") };
        var a = new GridActionGroup("G", "bi-x", actions);
        var b = new GridActionGroup("G", "bi-x", actions);

        a.Should().Be(b);
    }
}
