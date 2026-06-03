namespace Stratum.Common.Abstractions.Tests.Unit.FieldChange;

using FluentAssertions;
using Stratum.Common.Abstractions.FieldChange;
using Stratum.Common.Abstractions.UiRules;
using Xunit;

public sealed class FieldChangeResultTests
{
    [Fact]
    public void Empty_ShouldReturnEmptyResult()
    {
        var result = FieldChangeResult.Empty();

        result.FieldsToSet.Should().BeEmpty();
        result.UiAttributes.Should().BeNull();
    }

    [Fact]
    public void WithFields_ShouldSetFields()
    {
        var fields = new Dictionary<string, object?> { ["Name"] = "Acme", ["Code"] = null };

        var result = FieldChangeResult.WithFields(fields);

        result.FieldsToSet.Should().HaveCount(2);
        result.FieldsToSet["Name"].Should().Be("Acme");
        result.FieldsToSet["Code"].Should().BeNull();
        result.UiAttributes.Should().BeNull();
    }

    [Fact]
    public void WithFieldsAndUi_ShouldSetBoth()
    {
        var fields = new Dictionary<string, object?> { ["Amount"] = 100m };
        var ui = new UiAttributeSet(new Dictionary<string, UiFieldAttributes>
        {
            ["Discount"] = new UiFieldAttributes { Hidden = true },
        });

        var result = FieldChangeResult.WithFieldsAndUi(fields, ui);

        result.FieldsToSet.Should().ContainKey("Amount");
        result.UiAttributes.Should().NotBeNull();
        result.UiAttributes!["Discount"].Hidden.Should().BeTrue();
    }

    [Fact]
    public void Merge_OverlappingFields_LastWins()
    {
        var r1 = FieldChangeResult.WithFields(new Dictionary<string, object?> { ["Name"] = "A", ["Code"] = "X" });
        var r2 = FieldChangeResult.WithFields(new Dictionary<string, object?> { ["Name"] = "B" });

        var merged = FieldChangeResult.Merge([r1, r2]);

        merged.FieldsToSet["Name"].Should().Be("B");
        merged.FieldsToSet["Code"].Should().Be("X");
    }

    [Fact]
    public void Merge_EmptySequence_ShouldReturnEmpty()
    {
        var merged = FieldChangeResult.Merge([]);

        merged.FieldsToSet.Should().BeEmpty();
        merged.UiAttributes.Should().BeNull();
    }

    [Fact]
    public void Merge_WithUiAttributes_ShouldMergePropertyLevel()
    {
        var ui1 = new UiAttributeSet(new Dictionary<string, UiFieldAttributes>
        {
            ["Name"] = new UiFieldAttributes { Hidden = true, Required = true },
        });
        var ui2 = new UiAttributeSet(new Dictionary<string, UiFieldAttributes>
        {
            ["Name"] = new UiFieldAttributes { ReadOnly = true },
        });

        var r1 = FieldChangeResult.WithFieldsAndUi(new Dictionary<string, object?>(), ui1);
        var r2 = FieldChangeResult.WithFieldsAndUi(new Dictionary<string, object?>(), ui2);

        var merged = FieldChangeResult.Merge([r1, r2]);

        merged.UiAttributes.Should().NotBeNull();
        var nameAttrs = merged.UiAttributes!["Name"];
        nameAttrs.Hidden.Should().BeTrue();
        nameAttrs.Required.Should().BeTrue();
        nameAttrs.ReadOnly.Should().BeTrue();
    }

    [Fact]
    public void Merge_OnlyFirstHasUi_ShouldPreserveIt()
    {
        var ui = new UiAttributeSet(new Dictionary<string, UiFieldAttributes>
        {
            ["Status"] = new UiFieldAttributes { ReadOnly = true },
        });

        var r1 = FieldChangeResult.WithFieldsAndUi(new Dictionary<string, object?>(), ui);
        var r2 = FieldChangeResult.Empty();

        var merged = FieldChangeResult.Merge([r1, r2]);

        merged.UiAttributes.Should().NotBeNull();
        merged.UiAttributes!["Status"].ReadOnly.Should().BeTrue();
    }
}
