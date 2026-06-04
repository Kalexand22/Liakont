namespace Liakont.Modules.TvaMapping.Domain.Mapping;

using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.TvaMapping.Domain.Entities;

/// <summary>
/// Trace d'audit d'un mapping TVA réussi (item TVA02 §2, F03 §4.2). Instantané IMMUABLE et
/// auto-suffisant : il consigne l'entrée (code source, part), la table appliquée (version, validateur,
/// état de validation), la règle retenue (rang) et le résultat produit (catégorie, taux, VATEX). La
/// trace prouve, des années après, QUELLE règle de QUELLE version de table a produit QUEL motif
/// d'exonération — elle est attachée à la ligne mappée et persistée par le module Documents
/// (TRK01/04). Les champs résultat sont volontairement dupliqués du <see cref="MappingResult"/> :
/// l'enregistrement d'audit doit rester complet indépendamment de l'objet de résultat transitoire.
/// </summary>
public sealed record MappingTrace
{
    /// <summary>Horodatage du mapping (UTC), fourni par l'appelant.</summary>
    public required DateTimeOffset MappedAt { get; init; }

    /// <summary>Version de la table de mapping appliquée (traçabilité F03 §5).</summary>
    public required string MappingVersion { get; init; }

    /// <summary>Identité du valideur de la table (expert-comptable), <c>null</c> si non validée.</summary>
    public string? ValidatedBy { get; init; }

    /// <summary>Date de validation humaine de la table, <c>null</c> si non validée.</summary>
    public DateOnly? ValidatedDate { get; init; }

    /// <summary>
    /// État de validation de la table au moment du mapping. <c>false</c> = « NON VALIDÉE » : le mapping
    /// reste calculé (dev/démo), mais le garde-fou d'envoi en production (PIP01/TVA04) s'en sert pour
    /// refuser un envoi réel (item TVA01 §5, INV-006).
    /// </summary>
    public required bool IsValidated { get; init; }

    /// <summary>Code régime source ayant déclenché le mapping (écho de l'entrée).</summary>
    public required string InputRegimeCode { get; init; }

    /// <summary>Part de ligne mappée (écho de l'entrée).</summary>
    public required MappingPart Part { get; init; }

    /// <summary>Rang (1-based) de la règle appliquée dans l'ordre de déclaration de la table.</summary>
    public required int RuleOrdinal { get; init; }

    /// <summary>Libellé de la règle appliquée (aide opérateur), <c>null</c> si absent.</summary>
    public string? RuleLabel { get; init; }

    /// <summary>Catégorie de TVA produite (code UNCL5305, EN 16931 BT-151).</summary>
    public required VatCategory Category { get; init; }

    /// <summary>Mode de taux de la règle appliquée (fixe ou calculé depuis la source).</summary>
    public required RateMode RateMode { get; init; }

    /// <summary>
    /// Taux résolu (pourcentage, <c>decimal</c> exact — jamais flottant, CLAUDE.md n°1) quand le mode
    /// est <see cref="Entities.RateMode.Fixed"/> ; <c>null</c> quand le mode est
    /// <see cref="Entities.RateMode.ComputedFromSource"/> (le taux numérique est résolu en aval, par le
    /// pipeline qui dispose des montants de la ligne — F03 §4.1).
    /// </summary>
    public decimal? Rate { get; init; }

    /// <summary>Code VATEX produit (motif d'exonération, EN 16931 BT-121), <c>null</c> si non applicable.</summary>
    public string? Vatex { get; init; }
}
