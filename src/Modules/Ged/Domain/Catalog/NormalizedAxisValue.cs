namespace Liakont.Modules.Ged.Domain.Catalog;

using System;

/// <summary>
/// Résultat de <see cref="ValueNormalizer.Normalize"/> : la valeur d'axe rangée dans SA colonne typée
/// (jamais un <c>value text</c> fourre-tout — anti-EAV, INV-GED-01) plus un <see cref="NormalizedValue"/>
/// canonique pour le tri / la facette / la recherche. <b>Exactement une</b> colonne de valeur typée est
/// renseignée (miroir Domain de <c>ck_dal_value_or_retraction</c>) ; les fabriques statiques garantissent
/// cet invariant. Les colonnes non pertinentes restent <see langword="null"/>.
/// </summary>
public sealed record NormalizedAxisValue
{
    private NormalizedAxisValue(
        AxisDataType dataType,
        string? normalizedValue,
        string? valueString = null,
        decimal? valueNumber = null,
        DateOnly? valueDate = null,
        bool? valueBoolean = null,
        Guid? valueEntityId = null,
        string? valueJson = null)
    {
        DataType = dataType;
        NormalizedValue = normalizedValue;
        ValueString = valueString;
        ValueNumber = valueNumber;
        ValueDate = valueDate;
        ValueBoolean = valueBoolean;
        ValueEntityId = valueEntityId;
        ValueJson = valueJson;
    }

    /// <summary>Type d'axe dont provient la valeur.</summary>
    public AxisDataType DataType { get; }

    /// <summary>
    /// Forme canonique de la valeur. Pour <c>string</c>/<c>enum</c> (casefold) et <c>date</c> (ISO), c'est
    /// directement une clé lexicalement monotone, utilisable pour le tri / la facette / la recherche. Pour
    /// un axe <c>number</c>, ce n'est PAS une clé de tri : c'est une forme canonique à échelle FIXE
    /// (<c>value_scale</c>) — ou à échelle MINIMALE, zéros de fin retirés, quand l'axe ne déclare pas d'échelle —
    /// pour l'affichage / la déduplication ; le tri et les plages numériques doivent se faire sur la colonne
    /// typée <see cref="ValueNumber"/> (<c>decimal</c>). <see langword="null"/> pour un axe <c>json</c>
    /// (présentation-only, non facetté — INV-GED-04).
    /// </summary>
    public string? NormalizedValue { get; }

    /// <summary>Colonne <c>value_string</c> (axes <c>string</c> et <c>enum</c>).</summary>
    public string? ValueString { get; }

    /// <summary>Colonne <c>value_number</c> — <c>decimal</c> exact, échelle portée par l'axe (jamais double/float).</summary>
    public decimal? ValueNumber { get; }

    /// <summary>Colonne <c>value_date</c>.</summary>
    public DateOnly? ValueDate { get; }

    /// <summary>Colonne <c>value_boolean</c>.</summary>
    public bool? ValueBoolean { get; }

    /// <summary>Colonne <c>value_entity_id</c> (axe <c>entity</c>).</summary>
    public Guid? ValueEntityId { get; }

    /// <summary>Colonne <c>value_json</c> (présentation-only).</summary>
    public string? ValueJson { get; }

    internal static NormalizedAxisValue ForString(string value, string normalized) =>
        new(AxisDataType.Text, normalized, valueString: value);

    internal static NormalizedAxisValue ForEnum(string value, string normalized) =>
        new(AxisDataType.Enum, normalized, valueString: value);

    internal static NormalizedAxisValue ForNumber(decimal value, string normalized) =>
        new(AxisDataType.Number, normalized, valueNumber: value);

    internal static NormalizedAxisValue ForDate(DateOnly value, string normalized) =>
        new(AxisDataType.Date, normalized, valueDate: value);

    internal static NormalizedAxisValue ForBoolean(bool value, string normalized) =>
        new(AxisDataType.Boolean, normalized, valueBoolean: value);

    internal static NormalizedAxisValue ForEntity(Guid value, string normalized) =>
        new(AxisDataType.Entity, normalized, valueEntityId: value);

    internal static NormalizedAxisValue ForJson(string value) =>
        new(AxisDataType.Json, normalizedValue: null, valueJson: value);
}
