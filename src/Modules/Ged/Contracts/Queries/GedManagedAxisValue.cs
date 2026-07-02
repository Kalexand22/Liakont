namespace Liakont.Modules.Ged.Contracts.Queries;

/// <summary>
/// Une valeur d'axe COURANTE portée par un document géré (vue <c>current_axis_links</c>, F19 §3.4.3), telle
/// que restituée à la fiche document (GED09b). Un axe multi-valeur produit plusieurs entrées. Les valeurs sont
/// exposées TYPÉES (jamais un <c>value text</c> fourre-tout) : la mise en forme d'affichage (decimal fr-FR,
/// date, booléen) est faite par la couche de présentation (Host), le module ne fait AUCUNE mise en forme
/// culturelle. Un axe CONFIDENTIEL sans le droit <c>liakont.ged.confidential</c> est EXCLU server-side (§6.5) —
/// il n'apparaît jamais ici (anti-oracle).
/// </summary>
public sealed record GedManagedAxisValue
{
    /// <summary>Clé machine stable de l'axe (paramétrage tenant).</summary>
    public required string Code { get; init; }

    /// <summary>Libellé opérateur (FR) de l'axe.</summary>
    public required string Label { get; init; }

    /// <summary>Type technique de l'axe (<c>string|date|number|boolean|enum|entity|json</c>).</summary>
    public required string DataType { get; init; }

    /// <summary>Unité informative (<c>EUR</c>, <c>m2</c>…), ou <see langword="null"/>.</summary>
    public string? Unit { get; init; }

    /// <summary>Échelle décimale d'un axe <c>number</c> (2 = EUR, 0 = entier ; <see langword="null"/> = brut).</summary>
    public int? ValueScale { get; init; }

    /// <summary>Valeur chaîne (axe <c>string</c>/<c>enum</c>), ou <see langword="null"/>.</summary>
    public string? ValueString { get; init; }

    /// <summary>Valeur numérique EXACTE (axe <c>number</c>, <c>decimal</c> — jamais double/float), ou <see langword="null"/>.</summary>
    public decimal? ValueNumber { get; init; }

    /// <summary>Valeur date au format ISO <c>yyyy-MM-dd</c> (axe <c>date</c>), ou <see langword="null"/>.</summary>
    public string? ValueDate { get; init; }

    /// <summary>Valeur booléenne (axe <c>boolean</c>), ou <see langword="null"/>.</summary>
    public bool? ValueBoolean { get; init; }

    /// <summary>Libellé de l'instance d'entité liée (axe <c>entity</c>), ou <see langword="null"/>.</summary>
    public string? ValueEntityName { get; init; }

    /// <summary>Valeur normalisée (tri/recherche, repli d'affichage), ou <see langword="null"/>.</summary>
    public string? NormalizedValue { get; init; }
}
