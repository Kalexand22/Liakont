namespace Liakont.Modules.Ged.Tests.Unit.Graph;

using System;
using FluentAssertions;
using Liakont.Modules.Ged.Domain.Graph;
using Xunit;

/// <summary>
/// Validation Domain d'une <see cref="RelationInferenceRule"/> (GED24) : mode dans le vocabulaire fermé, borne de
/// profondeur dans [1..<see cref="RelationInferenceRule.MaxAllowedDepth"/>] (anti-DoS), genre non vide.
/// </summary>
public sealed class RelationInferenceRuleTests
{
    [Theory]
    [InlineData(RelationInferenceMode.Transitive)]
    [InlineData(RelationInferenceMode.Hierarchical)]
    public void Accepts_a_valid_rule(string mode)
    {
        var rule = new RelationInferenceRule("kind", mode, 3);

        rule.RelationKind.Should().Be("kind");
        rule.Mode.Should().Be(mode);
        rule.MaxDepth.Should().Be(3);
    }

    [Fact]
    public void Rejects_a_mode_outside_the_closed_vocabulary()
    {
        var act = () => new RelationInferenceRule("kind", "recursive", 3);

        act.Should().Throw<ArgumentException>().WithMessage("*Mode d'inférence*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(RelationInferenceRule.MaxAllowedDepth + 1)]
    public void Rejects_a_depth_outside_the_anti_dos_bound(int depth)
    {
        var act = () => new RelationInferenceRule("kind", RelationInferenceMode.Transitive, depth);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_blank_relation_kind(string kind)
    {
        var act = () => new RelationInferenceRule(kind, RelationInferenceMode.Transitive, 3);

        act.Should().Throw<ArgumentException>();
    }
}
