namespace Liakont.Modules.Ged.Tests.Unit.Catalog;

using System;
using FluentAssertions;
using Liakont.Modules.Ged.Domain.Catalog;
using Xunit;

/// <summary>
/// Couvre le « Domain catalogue polymorphe » de GED03a : le type d'entité est porté par un code libre (jamais
/// un enum figé, INV-GED-12) et le système de types d'axe est un vocabulaire TECHNIQUE fermé, miroir exact de
/// la contrainte SQL <c>ck_axis_def_data_type</c>.
/// </summary>
public sealed class CatalogModelTests
{
    [Theory]
    [InlineData("chantier")]
    [InlineData("dossier_rh")]
    [InlineData("contrat")]
    public void EntityType_accepts_any_business_code_without_a_fixed_enum(string code)
    {
        // Polymorphisme : n'importe quel code métier est accepté SANS toucher au code (paramétrage tenant).
        var entityType = new EntityType(code, label: "Libellé", identityKey: "siret");

        entityType.Code.Should().Be(code);
        entityType.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EntityType_refuses_an_empty_code(string code)
    {
        var act = () => new EntityType(code, "Libellé");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EntityType_refuses_an_empty_label()
    {
        var act = () => new EntityType("chantier", label: "  ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EntityType_normalizes_a_blank_identity_key_to_null()
    {
        var entityType = new EntityType("chantier", "Chantier", identityKey: "   ");

        entityType.IdentityKey.Should().BeNull();
    }

    [Theory]
    [InlineData(AxisDataType.Text, "string")]
    [InlineData(AxisDataType.Date, "date")]
    [InlineData(AxisDataType.Number, "number")]
    [InlineData(AxisDataType.Boolean, "boolean")]
    [InlineData(AxisDataType.Enum, "enum")]
    [InlineData(AxisDataType.Entity, "entity")]
    [InlineData(AxisDataType.Json, "json")]
    public void AxisDataType_round_trips_through_its_sql_code(AxisDataType dataType, string sqlCode)
    {
        dataType.ToSqlCode().Should().Be(sqlCode);
        AxisDataTypes.Parse(sqlCode).Should().Be(dataType);
    }

    [Theory]
    [InlineData("String")]
    [InlineData("decimal")]
    [InlineData("")]
    public void AxisDataType_parse_refuses_a_code_outside_the_closed_vocabulary(string sqlCode)
    {
        // La casse compte (le code SQL est minuscule) et le vocabulaire est fermé (jamais deviner).
        var act = () => AxisDataTypes.Parse(sqlCode);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
