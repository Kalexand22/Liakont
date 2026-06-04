namespace Liakont.Modules.TvaMapping.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Ajoute une règle à la table de mapping TVA du tenant courant (item TVA05 §1). Le tenant est résolu
/// par le contexte (jamais passé par l'appelant — tenant-scoping, CLAUDE.md n°9). La règle est validée
/// structurellement ; toute mutation repasse la table « NON VALIDÉE » (item TVA05 §2) et est journalisée
/// (append-only) de façon atomique. Consommée par l'endpoint API04 et la page WEB07.
/// </summary>
/// <remarks>
/// Les énumérations sont reçues par leur nom (string), comme le DTO de lecture : <see cref="Category"/>
/// est un code UNCL5305 (S, AA, AAA, Z, E, AE, G, K, O), <see cref="Part"/> et <see cref="RateMode"/>
/// les noms d'énumération. Une valeur hors liste est rejetée par le handler (jamais devinée).
/// </remarks>
public sealed record AddMappingRuleCommand : ICommand
{
    /// <summary>Code du régime TVA dans le système source.</summary>
    public required string SourceRegimeCode { get; init; }

    /// <summary>Libellé lisible du régime source, facultatif.</summary>
    public string? Label { get; init; }

    /// <summary>Part concernée (<c>Adjudication</c> / <c>Frais</c> / <c>Autre</c>).</summary>
    public required string Part { get; init; }

    /// <summary>Conditions supplémentaires sur des flags source (nom → valeur attendue), facultatives.</summary>
    public IReadOnlyDictionary<string, string>? SourceFlags { get; init; }

    /// <summary>Catégorie de TVA produite (code UNCL5305).</summary>
    public required string Category { get; init; }

    /// <summary>Code VATEX (motif d'exonération), obligatoire pour une catégorie E.</summary>
    public string? Vatex { get; init; }

    /// <summary>Note de paramétrage, facultative.</summary>
    public string? Note { get; init; }

    /// <summary>Mode de taux (<c>Fixed</c> / <c>ComputedFromSource</c>).</summary>
    public required string RateMode { get; init; }

    /// <summary>Taux fixe (pourcentage, <c>decimal</c> exact — CLAUDE.md n°1) ; <c>null</c> si calculé.</summary>
    public decimal? RateValue { get; init; }
}
