namespace Stratum.Common.UI.Tests.Unit;

using FluentAssertions;
using Stratum.Common.UI.Models;
using Xunit;

public sealed class NavNodeTests
{
    [Fact]
    public void WithChildrenHasChildrenIsTrue()
    {
        var node = new NavNode
        {
            Label = "Module",
            Children = [new NavNode { Label = "Child" }],
        };

        node.HasChildren.Should().BeTrue();
    }

    [Fact]
    public void WithoutChildrenHasChildrenIsFalse()
    {
        var node = new NavNode
        {
            Label = "Leaf",
            Href = "/some-page",
        };

        node.HasChildren.Should().BeFalse();
    }

    [Fact]
    public void SupportsThreeLevelsOfNesting()
    {
        var node = new NavNode
        {
            Label = "Level 0",
            Icon = "📊",
            Order = 0,
            Children =
            [
                new NavNode
                {
                    Label = "Level 1",
                    Children =
                    [
                        new NavNode { Label = "Level 2", Href = "/deep-page" },
                    ],
                },
            ],
        };

        node.HasChildren.Should().BeTrue();
        node.Children[0].HasChildren.Should().BeTrue();
        node.Children[0].Children[0].HasChildren.Should().BeFalse();
        node.Children[0].Children[0].Href.Should().Be("/deep-page");
    }

    [Fact]
    public void DefaultValuesAreCorrect()
    {
        var node = new NavNode { Label = "Test" };

        node.Icon.Should().BeNull();
        node.Href.Should().BeNull();
        node.Children.Should().BeEmpty();
        node.Order.Should().Be(0);
        node.ExactMatch.Should().BeFalse();
    }

    [Fact]
    public void CanHaveBothHrefAndChildren()
    {
        var node = new NavNode
        {
            Label = "Clickable Section",
            Href = "/section",
            Children = [new NavNode { Label = "Sub-item", Href = "/section/sub" }],
        };

        node.Href.Should().NotBeNull();
        node.HasChildren.Should().BeTrue();
    }
}
