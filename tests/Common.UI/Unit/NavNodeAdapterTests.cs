namespace Stratum.Common.UI.Tests.Unit;

using FluentAssertions;
using Stratum.Common.UI.Models;
using Xunit;

public sealed class NavNodeAdapterTests
{
    [Fact]
    public void ToNavNodeConvertsSectionToRootNode()
    {
        var section = new NavSection(
            Title: "Home",
            Icon: "📊",
            Order: 0,
            Items: [new NavItem("Dashboard", "/", ExactMatch: true)]);

        var node = section.ToNavNode();

        node.Label.Should().Be("Home");
        node.Icon.Should().Be("📊");
        node.Order.Should().Be(0);
        node.HasChildren.Should().BeTrue();
        node.Children.Should().HaveCount(1);
        node.Children[0].Label.Should().Be("Dashboard");
        node.Children[0].Href.Should().Be("/");
        node.Children[0].ExactMatch.Should().BeTrue();
    }

    [Fact]
    public void ToNavNodeConvertsMultipleItems()
    {
        var section = new NavSection(
            Title: "Showcase",
            Icon: "🧩",
            Order: 90,
            Items:
            [
                new NavItem("Dashboard", "/showcase", ExactMatch: true),
                new NavItem("Products", "/showcase/products"),
                new NavItem("Orders", "/showcase/orders"),
            ]);

        var node = section.ToNavNode();

        node.Children.Should().HaveCount(3);
        node.Children[0].Label.Should().Be("Dashboard");
        node.Children[1].Label.Should().Be("Products");
        node.Children[2].Label.Should().Be("Orders");
    }

    [Fact]
    public void ToNavNodeEmptySectionHasNoChildren()
    {
        var section = new NavSection("Empty", "📁", 50, []);

        var node = section.ToNavNode();

        node.HasChildren.Should().BeFalse();
        node.Children.Should().BeEmpty();
    }

    [Fact]
    public void ToNavNodesOrdersSectionsByOrder()
    {
        var sections = new[]
        {
            new NavSection("ZLast", "🔚", 90, [new NavItem("A", "/a")]),
            new NavSection("AFirst", "🔛", 0, [new NavItem("B", "/b")]),
            new NavSection("MMiddle", "🔜", 50, [new NavItem("C", "/c")]),
        };

        var nodes = sections.ToNavNodes();

        nodes.Should().HaveCount(3);
        nodes[0].Label.Should().Be("AFirst");
        nodes[1].Label.Should().Be("MMiddle");
        nodes[2].Label.Should().Be("ZLast");
    }

    [Fact]
    public void BuildNavTreeCollectsFromProviders()
    {
        var providers = new INavSectionProvider[]
        {
            new TestNavProvider(new NavSection("Sales", "📋", 20, [new NavItem("Quotes", "/quotes")])),
            new TestNavProvider(new NavSection("Home", "📊", 0, [new NavItem("Dashboard", "/", ExactMatch: true)])),
        };

        var nodes = providers.BuildNavTree();

        nodes.Should().HaveCount(2);
        nodes[0].Label.Should().Be("Home");
        nodes[0].Order.Should().Be(0);
        nodes[1].Label.Should().Be("Sales");
        nodes[1].Order.Should().Be(20);
    }

    [Fact]
    public void ToNavNodeThrowsOnNull()
    {
        NavSection? section = null;

        var act = () => section!.ToNavNode();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ToNavNodesThrowsOnNull()
    {
        IEnumerable<NavSection>? sections = null;

        var act = () => sections!.ToNavNodes();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildNavTreeThrowsOnNull()
    {
        IEnumerable<INavSectionProvider>? providers = null;

        var act = () => providers!.BuildNavTree();

        act.Should().Throw<ArgumentNullException>();
    }

    private sealed class TestNavProvider(NavSection section) : INavSectionProvider
    {
        public NavSection GetSection() => section;
    }
}
