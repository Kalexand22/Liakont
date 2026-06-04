namespace Liakont.Modules.TvaMapping.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Remplace les valeurs d'une règle existante de la table du tenant courant (item TVA05 §1). La règle
/// est IDENTIFIÉE par le couple (<see cref="SourceRegimeCode"/>, <see cref="Part"/>) — ce couple ne
/// change pas (pour changer la clé, supprimer puis ajouter). Les autres champs (catégorie, taux, VATEX,
/// flags, libellé, note) sont remplacés. Lève une erreur « introuvable » si aucune règle ne correspond.
/// Toute mutation repasse la table « NON VALIDÉE » (item TVA05 §2) et est journalisée atomiquement.
/// </summary>
public sealed record UpdateMappingRuleCommand : ICommand
{
    /// <summary>Code régime de la règle à modifier (clé d'identification, inchangé).</summary>
    public required string SourceRegimeCode { get; init; }

    /// <summary>Part de la règle à modifier (clé d'identification, inchangée).</summary>
    public required string Part { get; init; }

    /// <summary>Nouveau libellé lisible du régime source, facultatif.</summary>
    public string? Label { get; init; }

    /// <summary>Nouvelles conditions sur des flags source (nom → valeur attendue), facultatives.</summary>
    public IReadOnlyDictionary<string, string>? SourceFlags { get; init; }

    /// <summary>Nouvelle catégorie de TVA produite (code UNCL5305).</summary>
    public required string Category { get; init; }

    /// <summary>Nouveau code VATEX (motif d'exonération), obligatoire pour une catégorie E.</summary>
    public string? Vatex { get; init; }

    /// <summary>Nouvelle note de paramétrage, facultative.</summary>
    public string? Note { get; init; }

    /// <summary>Nouveau mode de taux (<c>Fixed</c> / <c>ComputedFromSource</c>).</summary>
    public required string RateMode { get; init; }

    /// <summary>Nouveau taux fixe (pourcentage, <c>decimal</c>) ; <c>null</c> si calculé.</summary>
    public decimal? RateValue { get; init; }
}
