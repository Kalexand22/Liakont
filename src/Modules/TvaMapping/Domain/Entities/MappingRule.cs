namespace Liakont.Modules.TvaMapping.Domain.Entities;

using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Règle de mapping TVA (F03 §4.1, item TVA01 §2) : condition riche sur le régime source →
/// triplet normalisé {catégorie EN 16931, taux, VATEX}. Une règle matche sur le code régime, la
/// <see cref="Part"/> et, optionnellement, des <see cref="SourceFlags"/> supplémentaires — pas
/// seulement sur le code (F03 §3 : un même code peut produire des mappings différents selon
/// <c>RegimeMarge</c> / <c>assujetti_tva</c>).
/// </summary>
/// <remarks>
/// Value object immuable. La cohérence fiscale (catégorie UNCL5305, E+0 % → VATEX, mode de taux)
/// est vérifiée par <see cref="Services.MappingTableValidator"/> à la création ET au chargement de
/// la table porteuse — jamais une règle invalide n'est silencieusement acceptée (CLAUDE.md n°2/3).
/// </remarks>
public sealed record MappingRule
{
    /// <summary>Code du régime TVA dans le système source (propre à chaque logiciel).</summary>
    public required string SourceRegimeCode { get; init; }

    /// <summary>Libellé lisible du régime source (aide opérateur), facultatif.</summary>
    public string? Label { get; init; }

    /// <summary>Part de la ligne concernée (adjudication / frais / autre).</summary>
    public required MappingPart Part { get; init; }

    /// <summary>
    /// Conditions supplémentaires sur des flags du document source (nom du flag → valeur attendue),
    /// <c>null</c> si la règle ne dépend que du code et de la part. GÉNÉRIQUE : les noms de flags
    /// (ex. <c>RegimeMarge</c>, <c>assujetti_tva</c> d'un logiciel d'enchères) sont des EXEMPLES de
    /// paramétrage tenant, jamais codés en dur dans le produit (CLAUDE.md n°7).
    /// </summary>
    public IReadOnlyDictionary<string, string>? SourceFlags { get; init; }

    /// <summary>Catégorie de TVA produite (code UNCL5305, EN 16931 BT-151 — F03 §2.1).</summary>
    public required VatCategory Category { get; init; }

    /// <summary>
    /// Code VATEX (motif d'exonération, EN 16931 BT-121), obligatoire pour une exonération à 0 %
    /// (catégorie <see cref="VatCategory.E"/> à taux fixe 0) — F03 §2.2. <c>null</c> sinon.
    /// </summary>
    public string? Vatex { get; init; }

    /// <summary>Note de paramétrage (ex. point à valider par l'expert-comptable), facultative.</summary>
    public string? Note { get; init; }

    /// <summary>Mode de détermination du taux (fixe ou calculé depuis la source).</summary>
    public required RateMode RateMode { get; init; }

    /// <summary>
    /// Taux fixe (en pourcentage, <c>decimal</c> exact — jamais de flottant, CLAUDE.md n°1) quand
    /// <see cref="RateMode"/> vaut <see cref="RateMode.Fixed"/> ; <c>null</c> quand le taux est
    /// <see cref="RateMode.ComputedFromSource"/> (résolu par le moteur à l'exécution).
    /// </summary>
    public decimal? RateValue { get; init; }
}
