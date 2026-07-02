namespace Liakont.Modules.Ged.Tests.Unit.Mapping;

using System;
using FluentAssertions;
using Liakont.Modules.Ged.Domain.Mapping;
using Xunit;

/// <summary>
/// Tests unitaires de <see cref="GedMappingProfile"/> (F19 §4.5, GED12 ; miroir de <c>MappingTable</c>) :
/// validation structurelle à la construction (type/version, code d'axe dupliqué, sélecteur mal formé,
/// validation cohérente) et sémantique <b>validé / non validé</b> (jamais appliqué non validé).
/// </summary>
public sealed class GedMappingProfileTests
{
    [Fact]
    public void A_profile_with_validator_and_date_is_validated()
    {
        var profile = MinimalProfile("ec@example.test", new DateOnly(2026, 1, 1));

        profile.IsValidated.Should().BeTrue();
    }

    [Fact]
    public void A_profile_without_validation_is_not_validated()
    {
        var profile = MinimalProfile(validatedBy: null, validatedDate: null);

        profile.IsValidated.Should().BeFalse();
    }

    [Fact]
    public void An_incoherent_validation_is_rejected()
    {
        var act = () => MinimalProfile("ec@example.test", validatedDate: null);

        act.Should().Throw<InvalidGedMappingProfileException>().WithMessage("*validation*");
    }

    [Fact]
    public void An_empty_document_type_is_rejected()
    {
        var act = () => GedMappingProfile.Create(
            "  ",
            GedMappingProfile.InitialProfileVersion,
            storagePolicy: null,
            validatedBy: null,
            validatedDate: null,
            axisRules: Array.Empty<AxisMappingRule>(),
            entityRules: Array.Empty<EntityMappingRule>(),
            relationRules: Array.Empty<RelationMappingRule>(),
            createdAt: DateTimeOffset.UnixEpoch);

        act.Should().Throw<InvalidGedMappingProfileException>();
    }

    [Fact]
    public void A_duplicate_axis_code_is_rejected()
    {
        var act = () => GedMappingProfile.Create(
            "typ_a",
            GedMappingProfile.InitialProfileVersion,
            storagePolicy: null,
            validatedBy: null,
            validatedDate: null,
            axisRules: new[]
            {
                new AxisMappingRule("axe_x", "$.fields.a", IsRequired: false, IsMulti: false),
                new AxisMappingRule("axe_x", "$.fields.b", IsRequired: false, IsMulti: false),
            },
            entityRules: Array.Empty<EntityMappingRule>(),
            relationRules: Array.Empty<RelationMappingRule>(),
            createdAt: DateTimeOffset.UnixEpoch);

        act.Should().Throw<InvalidGedMappingProfileException>().WithMessage("*dupliqué*");
    }

    [Fact]
    public void A_malformed_selector_is_rejected_at_construction()
    {
        var act = () => GedMappingProfile.Create(
            "typ_a",
            GedMappingProfile.InitialProfileVersion,
            storagePolicy: null,
            validatedBy: null,
            validatedDate: null,
            axisRules: new[] { new AxisMappingRule("axe_x", "fields.a", IsRequired: false, IsMulti: false) },
            entityRules: Array.Empty<EntityMappingRule>(),
            relationRules: Array.Empty<RelationMappingRule>(),
            createdAt: DateTimeOffset.UnixEpoch);

        act.Should().Throw<InvalidGedMappingProfileException>()
            .WithInnerException<InvalidGedSelectorException>();
    }

    [Fact]
    public void Invalidate_drops_the_validation_and_stamps_the_mutation()
    {
        var profile = MinimalProfile("ec@example.test", new DateOnly(2026, 1, 1));

        profile.Invalidate(DateTimeOffset.UnixEpoch.AddDays(1));

        profile.IsValidated.Should().BeFalse();
        profile.ValidatedBy.Should().BeNull();
        profile.ValidatedDate.Should().BeNull();
        profile.UpdatedAt.Should().Be(DateTimeOffset.UnixEpoch.AddDays(1));
    }

    private static GedMappingProfile MinimalProfile(string? validatedBy, DateOnly? validatedDate)
    {
        return GedMappingProfile.Create(
            "typ_a",
            GedMappingProfile.InitialProfileVersion,
            storagePolicy: null,
            validatedBy: validatedBy,
            validatedDate: validatedDate,
            axisRules: new[] { new AxisMappingRule("axe_x", "$.fields.a", IsRequired: false, IsMulti: false) },
            entityRules: Array.Empty<EntityMappingRule>(),
            relationRules: Array.Empty<RelationMappingRule>(),
            createdAt: DateTimeOffset.UnixEpoch);
    }
}
