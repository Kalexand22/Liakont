namespace Liakont.Modules.Ged.Tests.Unit.Catalog;

using System;
using System.Globalization;
using System.Reflection;
using FluentAssertions;
using Liakont.Modules.Ged.Domain.Catalog;
using Xunit;

/// <summary>
/// Couvre l'acceptance GED03a « <c>value_number</c> = decimal, échelle portée par l'axe, arrondi half-up » et
/// « validation valeur d'axe → refus si non conforme au data_type, jamais deviner (règle 2) » (F19 §3.3.1/§3.7).
/// </summary>
public sealed class ValueNormalizerTests
{
    [Fact]
    public void Number_is_stored_as_decimal_never_double()
    {
        // Garde de type CLAUDE.md n°1 : la colonne de valeur d'un axe number est un decimal, jamais double/float.
        var property = typeof(NormalizedAxisValue).GetProperty(nameof(NormalizedAxisValue.ValueNumber));
        property!.PropertyType.Should().Be<decimal?>();

        var result = ValueNormalizer.Normalize(AxisDataType.Number, valueScale: 2, "12.34");

        result.ValueNumber.Should().Be(12.34m);
        result.ValueString.Should().BeNull();
    }

    [Theory]
    [InlineData("1.005", 2, "1.01")]
    [InlineData("1.004", 2, "1.00")]
    [InlineData("-1.005", 2, "-1.01")]
    [InlineData("1.5", 0, "2")]
    [InlineData("2.5", 0, "3")]
    [InlineData("1234.5", 2, "1234.50")]
    public void Number_is_rounded_half_up_to_the_axis_scale(string raw, int scale, string expected)
    {
        // Arrondi commercial half-up (away-from-zero) à l'échelle de l'axe (F19 §3.3.1, comme PivotRounding).
        var result = ValueNormalizer.Normalize(AxisDataType.Number, scale, raw);

        result.ValueNumber.Should().Be(decimal.Parse(expected, CultureInfo.InvariantCulture));
        result.NormalizedValue.Should().Be(expected);
    }

    [Fact]
    public void Number_with_null_scale_keeps_the_raw_precision()
    {
        var result = ValueNormalizer.Normalize(AxisDataType.Number, valueScale: null, "1.23456");

        result.ValueNumber.Should().Be(1.23456m);
        result.NormalizedValue.Should().Be("1.23456");
    }

    [Theory]
    [InlineData("1.5", "1.5")]
    [InlineData("1.50", "1.5")]
    [InlineData("1.500", "1.5")]
    [InlineData("100.00", "100")]
    [InlineData("-1.50", "-1.5")]
    [InlineData("0.0", "0")]
    public void Number_with_null_scale_canonicalizes_trailing_zeros(string raw, string expectedNormalized)
    {
        // GDF09 : sans échelle déclarée, la forme canonique retire les zéros de fin (échelle minimale) — value_number
        // garde le decimal parsé EXACT, seule normalized_value est canonicalisée.
        var result = ValueNormalizer.Normalize(AxisDataType.Number, valueScale: null, raw);

        result.NormalizedValue.Should().Be(expectedNormalized);
        result.ValueNumber.Should().Be(decimal.Parse(raw, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Number_with_null_scale_maps_equal_values_to_the_same_facet_key()
    {
        // Golden GDF09 : « 1.5 » et « 1.50 » (axe number sans échelle) → MÊME normalized_value → un seul bucket de
        // facette / une seule clé de déduplication pour le même nombre.
        var plain = ValueNormalizer.Normalize(AxisDataType.Number, valueScale: null, "1.5");
        var padded = ValueNormalizer.Normalize(AxisDataType.Number, valueScale: null, "1.50");

        padded.NormalizedValue.Should().Be(plain.NormalizedValue);
    }

    [Fact]
    public void String_value_is_trimmed_consistently_with_its_canonical_key()
    {
        // GDF09 : value_string ne conserve pas les espaces de bord — cohérent avec la clé casefold (trimée) et
        // avec NormalizeEnum.
        var result = ValueNormalizer.Normalize(AxisDataType.Text, valueScale: null, "  Réf 42  ");

        result.ValueString.Should().Be("Réf 42");
        result.NormalizedValue.Should().Be("réf 42");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1,234")]
    [InlineData("1e3")]
    [InlineData("")]
    [InlineData("   ")]
    public void Number_refuses_a_non_decimal_value(string raw)
    {
        var act = () => ValueNormalizer.Normalize(AxisDataType.Number, valueScale: 2, raw);

        act.Should().Throw<AxisValueFormatException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    public void Number_refuses_an_out_of_range_axis_scale(int scale)
    {
        var act = () => ValueNormalizer.Normalize(AxisDataType.Number, scale, "1.00");

        act.Should().Throw<AxisValueFormatException>();
    }

    [Fact]
    public void Date_parses_iso_and_normalizes_iso()
    {
        var result = ValueNormalizer.Normalize(AxisDataType.Date, valueScale: null, "2026-07-01");

        result.ValueDate.Should().Be(new DateOnly(2026, 7, 1));
        result.NormalizedValue.Should().Be("2026-07-01");
    }

    [Theory]
    [InlineData("01/07/2026")]
    [InlineData("2026-13-01")]
    [InlineData("not-a-date")]
    public void Date_refuses_a_non_iso_value(string raw)
    {
        var act = () => ValueNormalizer.Normalize(AxisDataType.Date, valueScale: null, raw);

        act.Should().Throw<AxisValueFormatException>();
    }

    [Theory]
    [InlineData("true", true, "true")]
    [InlineData("false", false, "false")]
    [InlineData("TRUE", true, "true")]
    public void Boolean_parses_and_normalizes(string raw, bool expected, string normalized)
    {
        var result = ValueNormalizer.Normalize(AxisDataType.Boolean, valueScale: null, raw);

        result.ValueBoolean.Should().Be(expected);
        result.NormalizedValue.Should().Be(normalized);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    public void Boolean_refuses_a_non_boolean_value(string raw)
    {
        var act = () => ValueNormalizer.Normalize(AxisDataType.Boolean, valueScale: null, raw);

        act.Should().Throw<AxisValueFormatException>();
    }

    [Fact]
    public void Entity_parses_a_guid()
    {
        var id = Guid.NewGuid();

        var result = ValueNormalizer.Normalize(AxisDataType.Entity, valueScale: null, id.ToString());

        result.ValueEntityId.Should().Be(id);
        result.NormalizedValue.Should().Be(id.ToString("D"));
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Entity_refuses_an_invalid_reference(string raw)
    {
        // Guid.Empty est refusé : une référence d'entité doit désigner une instance réelle.
        var act = () => ValueNormalizer.Normalize(AxisDataType.Entity, valueScale: null, raw);

        act.Should().Throw<AxisValueFormatException>();
    }

    [Fact]
    public void Enum_and_string_land_in_value_string_with_casefold_normalization()
    {
        var enumResult = ValueNormalizer.Normalize(AxisDataType.Enum, valueScale: null, "  Approuvé  ");
        enumResult.ValueString.Should().Be("Approuvé");
        enumResult.NormalizedValue.Should().Be("approuvé");
        enumResult.ValueNumber.Should().BeNull();

        var stringResult = ValueNormalizer.Normalize(AxisDataType.Text, valueScale: null, "Réf-42");
        stringResult.ValueString.Should().Be("Réf-42");
        stringResult.NormalizedValue.Should().Be("réf-42");
    }

    [Fact]
    public void Json_accepts_valid_json_and_has_no_normalized_facet_value()
    {
        var result = ValueNormalizer.Normalize(AxisDataType.Json, valueScale: null, """{"a":1}""");

        result.ValueJson.Should().Be("""{"a":1}""");

        // json est présentation-only (INV-GED-04) : pas de forme normalisée de facette/recherche.
        result.NormalizedValue.Should().BeNull();
    }

    [Theory]
    [InlineData("{not json}")]
    [InlineData("")]
    public void Json_refuses_an_invalid_fragment(string raw)
    {
        var act = () => ValueNormalizer.Normalize(AxisDataType.Json, valueScale: null, raw);

        act.Should().Throw<AxisValueFormatException>();
    }
}
