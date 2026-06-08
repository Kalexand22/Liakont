namespace Liakont.Host.TvaMappingTable;

using System.Collections.Generic;

/// <summary>
/// Modèle de saisie d'une règle de mapping TVA dans la console (item WEB07b). Neutre vis-à-vis de
/// MediatR : la page le collecte depuis le formulaire (listes FERMÉES pour catégorie / part / mode de
/// taux / VATEX — aucune saisie libre sur ces champs) et le service le traduit en commande TVA05
/// (<c>AddMappingRuleCommand</c> / <c>UpdateMappingRuleCommand</c>). Aucune validation fiscale ici :
/// la validation structurelle (catégorie E → VATEX, taux, doublon) reste du ressort des handlers
/// (CLAUDE.md n°2/3/19). Les codes transmis sont EXACTEMENT ceux des listes fermées.
/// </summary>
public sealed class TvaRuleFormModel
{
    /// <summary>Code du régime TVA dans le système source (clé, avec <see cref="Part"/>).</summary>
    public string SourceRegimeCode { get; set; } = string.Empty;

    /// <summary>Libellé lisible du régime source, facultatif.</summary>
    public string? Label { get; set; }

    /// <summary>Part concernée (code de liste fermée : Adjudication / Frais / Autre).</summary>
    public string Part { get; set; } = string.Empty;

    /// <summary>
    /// Conditions sur des flags source (nom → valeur attendue) de la règle, facultatives. NON éditables
    /// en console V1 : préservées telles quelles à la modification (jamais effacées silencieusement),
    /// <c>null</c> pour une nouvelle règle. Affichées en lecture seule quand présentes.
    /// </summary>
    public IReadOnlyDictionary<string, string>? SourceFlags { get; set; }

    /// <summary>Catégorie de TVA produite (code UNCL5305 de liste fermée).</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Code VATEX (liste fermée), obligatoire pour une catégorie E ; <c>null</c> sinon.</summary>
    public string? Vatex { get; set; }

    /// <summary>Note de paramétrage, facultative.</summary>
    public string? Note { get; set; }

    /// <summary>Mode de taux (code de liste fermée : Fixed / ComputedFromSource).</summary>
    public string RateMode { get; set; } = string.Empty;

    /// <summary>Taux fixe (pourcentage, <c>decimal</c> exact — CLAUDE.md n°1) ; <c>null</c> si calculé.</summary>
    public decimal? RateValue { get; set; }
}
