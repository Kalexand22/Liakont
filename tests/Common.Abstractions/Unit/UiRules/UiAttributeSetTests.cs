namespace Stratum.Common.Abstractions.Tests.Unit.UiRules;

using FluentAssertions;
using Stratum.Common.Abstractions.UiRules;
using Xunit;

public sealed class UiAttributeSetTests
{
    [Fact]
    public void Empty_ShouldHaveZeroCount()
    {
        var set = new UiAttributeSet();

        set.Count.Should().Be(0);
        set.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_WithEntries_ShouldPopulate()
    {
        var set = new UiAttributeSet(new Dictionary<string, UiFieldAttributes>
        {
            ["Name"] = new UiFieldAttributes { Required = true },
            ["Code"] = new UiFieldAttributes { ReadOnly = true },
        });

        set.Count.Should().Be(2);
        set["Name"].Required.Should().BeTrue();
        set["Code"].ReadOnly.Should().BeTrue();
    }

    [Fact]
    public void ContainsKey_ShouldReturnCorrectly()
    {
        var set = new UiAttributeSet(new Dictionary<string, UiFieldAttributes>
        {
            ["Name"] = new UiFieldAttributes(),
        });

        set.ContainsKey("Name").Should().BeTrue();
        set.ContainsKey("Missing").Should().BeFalse();
    }

    [Fact]
    public void TryGetValue_ShouldReturnCorrectly()
    {
        var set = new UiAttributeSet(new Dictionary<string, UiFieldAttributes>
        {
            ["Name"] = new UiFieldAttributes { Hidden = true },
        });

        set.TryGetValue("Name", out var attrs).Should().BeTrue();
        attrs.Hidden.Should().BeTrue();

        set.TryGetValue("Missing", out _).Should().BeFalse();
    }

    [Fact]
    public void Merge_DisjointFields_ShouldCombine()
    {
        var left = new UiAttributeSet(new Dictionary<string, UiFieldAttributes>
        {
            ["Name"] = new UiFieldAttributes { Required = true },
        });
        var right = new UiAttributeSet(new Dictionary<string, UiFieldAttributes>
        {
            ["Code"] = new UiFieldAttributes { ReadOnly = true },
        });

        var merged = UiAttributeSet.Merge(left, right);

        merged.Count.Should().Be(2);
        merged["Name"].Required.Should().BeTrue();
        merged["Code"].ReadOnly.Should().BeTrue();
    }

    [Fact]
    public void Merge_OverlappingFields_ShouldMergePropertyLevel()
    {
        var left = new UiAttributeSet(new Dictionary<string, UiFieldAttributes>
        {
            ["Name"] = new UiFieldAttributes { Hidden = true, Required = true },
        });
        var right = new UiAttributeSet(new Dictionary<string, UiFieldAttributes>
        {
            ["Name"] = new UiFieldAttributes { ReadOnly = true },
        });

        var merged = UiAttributeSet.Merge(left, right);

        var nameAttrs = merged["Name"];
        nameAttrs.Hidden.Should().BeTrue();
        nameAttrs.Required.Should().BeTrue();
        nameAttrs.ReadOnly.Should().BeTrue();
    }

    [Fact]
    public void Merge_DomainFilter_RightOverridesLeft()
    {
        var left = new UiAttributeSet(new Dictionary<string, UiFieldAttributes>
        {
            ["Partner"] = new UiFieldAttributes { DomainFilter = "status == 'Active'" },
        });
        var right = new UiAttributeSet(new Dictionary<string, UiFieldAttributes>
        {
            ["Partner"] = new UiFieldAttributes { DomainFilter = "type == 'Customer'" },
        });

        var merged = UiAttributeSet.Merge(left, right);

        merged["Partner"].DomainFilter.Should().Be("type == 'Customer'");
    }

    [Fact]
    public void Merge_DomainFilter_LeftPreservedWhenRightNull()
    {
        var left = new UiAttributeSet(new Dictionary<string, UiFieldAttributes>
        {
            ["Partner"] = new UiFieldAttributes { DomainFilter = "status == 'Active'" },
        });
        var right = new UiAttributeSet(new Dictionary<string, UiFieldAttributes>
        {
            ["Partner"] = new UiFieldAttributes { ReadOnly = true },
        });

        var merged = UiAttributeSet.Merge(left, right);

        merged["Partner"].DomainFilter.Should().Be("status == 'Active'");
        merged["Partner"].ReadOnly.Should().BeTrue();
    }

    [Fact]
    public void Merge_EmptySets_ShouldReturnEmpty()
    {
        var merged = UiAttributeSet.Merge(new UiAttributeSet(), new UiAttributeSet());

        merged.Should().BeEmpty();
    }
}
