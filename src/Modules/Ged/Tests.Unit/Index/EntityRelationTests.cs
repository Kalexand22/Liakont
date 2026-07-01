namespace Liakont.Modules.Ged.Tests.Unit.Index;

using System;
using FluentAssertions;
using Liakont.Modules.Ged.Domain.Index;
using Xunit;

/// <summary>
/// Validation Domain d'une <see cref="EntityRelation"/> à appender (GED24), miroir des contraintes
/// <c>ck_er_no_self</c> / <c>ck_er_relation_type</c> / <c>ck_er_source</c> / <c>ck_er_confidence</c>.
/// </summary>
public sealed class EntityRelationTests
{
    private static readonly Guid From = Guid.NewGuid();
    private static readonly Guid To = Guid.NewGuid();

    [Theory]
    [InlineData(EntityRelation.InferredRelationType)]
    [InlineData(EntityRelation.InheritedRelationType)]
    [InlineData("direct")]
    [InlineData("extracted")]
    public void Accepts_a_valid_derived_relation(string relationType)
    {
        var relation = new EntityRelation(From, To, "kind", relationType, "agent");

        relation.FromEntityId.Should().Be(From);
        relation.ToEntityId.Should().Be(To);
        relation.RelationType.Should().Be(relationType);
        relation.ConfidenceScore.Should().BeNull();
    }

    [Fact]
    public void Rejects_a_self_relation()
    {
        var same = Guid.NewGuid();
        var act = () => new EntityRelation(same, same, "kind", EntityRelation.InferredRelationType, "agent");

        act.Should().Throw<ArgumentException>().WithMessage("*réflexive*");
    }

    [Fact]
    public void Rejects_a_relation_type_outside_the_closed_vocabulary()
    {
        var act = () => new EntityRelation(From, To, "kind", "derived", "agent");

        act.Should().Throw<ArgumentException>().WithMessage("*Type de relation*");
    }

    [Fact]
    public void Rejects_a_source_outside_the_closed_vocabulary()
    {
        var act = () => new EntityRelation(From, To, "kind", EntityRelation.InferredRelationType, "bogus");

        act.Should().Throw<ArgumentException>().WithMessage("*Source de relation*");
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Rejects_a_confidence_score_out_of_range(double score)
    {
        var act = () => new EntityRelation(From, To, "kind", EntityRelation.InferredRelationType, "agent", (decimal)score);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rejects_a_blank_relation_kind()
    {
        var act = () => new EntityRelation(From, To, "  ", EntityRelation.InferredRelationType, "agent");

        act.Should().Throw<ArgumentException>();
    }
}
