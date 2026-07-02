namespace Liakont.Modules.Ged.Domain.Catalog;

using System;
using System.Globalization;
using System.Text.Json;

/// <summary>
/// Normalise une valeur d'axe BRUTE (chaîne) vers sa colonne typée + une forme canonique de tri/facette
/// (F19 §3.3.1/§3.7, Domain pur). Deux invariants produit :
/// <list type="number">
/// <item><description><b>Montant (règle 1)</b> : un axe <c>number</c> est un <c>decimal</c> C#, JAMAIS
/// <c>double</c>/<c>float</c> ; l'échelle est portée par l'axe (<c>value_scale</c>) et l'arrondi est
/// <b>commercial half-up</b> (<see cref="MidpointRounding.AwayFromZero"/>, valable aussi pour les valeurs
/// négatives), appliqué AVANT insert et matérialisé dans <c>normalized_value</c>.</description></item>
/// <item><description><b>Refus, jamais deviner (règle 2)</b> : une valeur qui ne correspond pas au
/// <see cref="AxisDataType"/> déclaré lève <see cref="AxisValueFormatException"/> — elle n'est jamais
/// interprétée « au mieux » ni tronquée en silence.</description></item>
/// </list>
/// La validation d'appartenance d'un code au vocabulaire d'un axe <c>enum</c> et l'existence d'une entité
/// cible ne sont PAS du ressort de ce normaliseur pur : elles sont faites par le catalogue / l'UoW
/// (<c>IAxisCatalog</c>, GED04) qui a accès à la base.
/// </summary>
public static class ValueNormalizer
{
    // Analyse decimal STRICTE et déterministe (invariant) : signe + point décimal uniquement. Refuse les
    // séparateurs de milliers, l'exposant et les cultures locales — la valeur brute d'un axe est canonique
    // (jamais deviner un format « au mieux »).
    private const NumberStyles DecimalStyle =
        NumberStyles.AllowLeadingSign
        | NumberStyles.AllowDecimalPoint
        | NumberStyles.AllowLeadingWhite
        | NumberStyles.AllowTrailingWhite;

    // Forme canonique de facette/dédup d'un axe number SANS échelle déclarée (value_scale null = « valeur brute »,
    // V005) : échelle MINIMALE, zéros de fin retirés, pour qu'un même nombre écrit « 1.5 » ou « 1.50 » produise
    // UNE seule clé (les facettes et la déduplication groupent sur normalized_value — sinon deux buckets pour le
    // même nombre). L'échelle max d'un decimal .NET est 28 : autant de « # » garantit qu'AUCUN chiffre significatif
    // n'est arrondi (exact, jamais double/float). Ce n'est PAS une clé de tri — le tri/les plages numériques se
    // font sur la colonne typée value_number (decimal).
    private static readonly string CanonicalNumberFormat = "0." + new string('#', 28);

    /// <summary>
    /// Normalise <paramref name="rawValue"/> selon <paramref name="dataType"/> et, pour un axe <c>number</c>,
    /// selon <paramref name="valueScale"/> (échelle décimale déclarée de l'axe ; <see langword="null"/> = brut).
    /// </summary>
    /// <exception cref="AxisValueFormatException">La valeur ne correspond pas au type d'axe déclaré.</exception>
    public static NormalizedAxisValue Normalize(AxisDataType dataType, int? valueScale, string rawValue)
    {
        if (rawValue is null || string.IsNullOrWhiteSpace(rawValue))
        {
            throw new AxisValueFormatException(dataType, rawValue ?? string.Empty, "la valeur est vide.");
        }

        return dataType switch
        {
            AxisDataType.Text => NormalizeString(rawValue),
            AxisDataType.Enum => NormalizeEnum(rawValue),
            AxisDataType.Number => NormalizeNumber(valueScale, rawValue),
            AxisDataType.Date => NormalizeDate(rawValue),
            AxisDataType.Boolean => NormalizeBoolean(rawValue),
            AxisDataType.Entity => NormalizeEntity(rawValue),
            AxisDataType.Json => NormalizeJson(rawValue),
            _ => throw new ArgumentOutOfRangeException(nameof(dataType), dataType, "Type d'axe GED inconnu."),
        };
    }

    // value_string reçoit la valeur TRIMÉE : cohérente avec la clé canonique (Casefold trime aussi, l.147) et avec
    // NormalizeEnum (l.65). Des espaces de bord ne doivent bruiter ni la colonne typée ni la divergence
    // value_string / normalized_value.
    private static NormalizedAxisValue NormalizeString(string rawValue) =>
        NormalizedAxisValue.ForString(rawValue.Trim(), Casefold(rawValue));

    // Le code enum est rangé en value_string ; l'appartenance au vocabulaire (ged_catalog.axis_values) est
    // validée par IAxisCatalog (GED04), pas ici (le normaliseur pur n'a pas accès à la base).
    private static NormalizedAxisValue NormalizeEnum(string rawValue) =>
        NormalizedAxisValue.ForEnum(rawValue.Trim(), Casefold(rawValue));

    private static NormalizedAxisValue NormalizeNumber(int? valueScale, string rawValue)
    {
        if (valueScale is < 0 or > 9)
        {
            // Miroir Domain de ck_axis_def_scale : une échelle hors [0..9] est une définition d'axe invalide.
            throw new AxisValueFormatException(
                AxisDataType.Number,
                rawValue,
                $"l'échelle déclarée de l'axe ({valueScale}) est hors de l'intervalle [0..9].");
        }

        if (!decimal.TryParse(rawValue, DecimalStyle, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new AxisValueFormatException(AxisDataType.Number, rawValue, $"« {rawValue} » n'est pas un nombre decimal.");
        }

        // Arrondi commercial half-up à l'échelle de l'axe (jamais double/float) quand l'axe DÉCLARE une échelle.
        // Forme canonique d'affichage/dédup : échelle FIXE (« F{scale} », largeur déterministe) — Math.Round ne
        // matérialise pas les zéros de fin (1234,5 arrondi à 2 resterait « 1234.5 »). Ce n'est PAS une clé lexicale :
        // le tri/la plage numérique se font sur la colonne typée value_number (decimal), jamais sur cette chaîne.
        if (valueScale is int scale)
        {
            var rounded = Math.Round(parsed, scale, MidpointRounding.AwayFromZero);
            return NormalizedAxisValue.ForNumber(
                rounded,
                rounded.ToString("F" + scale.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));
        }

        // Sans échelle déclarée (value_scale null) : value_number garde le decimal parsé EXACT (aucun arrondi, n°1) ;
        // seule normalized_value est canonicalisée à l'échelle MINIMALE (CanonicalNumberFormat) pour qu'un même
        // nombre ne se scinde pas en deux buckets de facette/dédup selon son écriture (« 1.5 » vs « 1.50 »).
        return NormalizedAxisValue.ForNumber(
            parsed,
            parsed.ToString(CanonicalNumberFormat, CultureInfo.InvariantCulture));
    }

    private static NormalizedAxisValue NormalizeDate(string rawValue)
    {
        // ISO-8601 calendaire stricte (yyyy-MM-dd) — pas d'heure, pas de culture locale.
        if (!DateOnly.TryParseExact(rawValue.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new AxisValueFormatException(AxisDataType.Date, rawValue, $"« {rawValue} » n'est pas une date ISO-8601 (yyyy-MM-dd).");
        }

        return NormalizedAxisValue.ForDate(date, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private static NormalizedAxisValue NormalizeBoolean(string rawValue)
    {
        if (!bool.TryParse(rawValue.Trim(), out var value))
        {
            throw new AxisValueFormatException(AxisDataType.Boolean, rawValue, $"« {rawValue} » n'est pas un booléen (true|false).");
        }

        return NormalizedAxisValue.ForBoolean(value, value ? "true" : "false");
    }

    private static NormalizedAxisValue NormalizeEntity(string rawValue)
    {
        if (!Guid.TryParse(rawValue.Trim(), out var entityId) || entityId == Guid.Empty)
        {
            throw new AxisValueFormatException(AxisDataType.Entity, rawValue, $"« {rawValue} » n'est pas un identifiant d'entité (uuid).");
        }

        return NormalizedAxisValue.ForEntity(entityId, entityId.ToString("D", CultureInfo.InvariantCulture));
    }

    private static NormalizedAxisValue NormalizeJson(string rawValue)
    {
        try
        {
            JsonDocument.Parse(rawValue).Dispose();
        }
        catch (JsonException ex)
        {
            throw new AxisValueFormatException(AxisDataType.Json, rawValue, $"le fragment n'est pas un JSON valide ({ex.Message}).");
        }

        // json est présentation-only (INV-GED-04) : aucune forme normalisée de facette/recherche.
        return NormalizedAxisValue.ForJson(rawValue);
    }

    // Casefold minimal, déterministe et invariant (tri/facette) : trim + minuscule invariante. L'unaccent
    // complet appartient à la normalisation de recherche (GED08), pas au Domain pur.
    private static string Casefold(string value) => value.Trim().ToLowerInvariant();
}
