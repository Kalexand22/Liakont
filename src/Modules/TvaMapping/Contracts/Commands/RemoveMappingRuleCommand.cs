namespace Liakont.Modules.TvaMapping.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Supprime une règle de la table de mapping TVA du tenant courant (item TVA05 §1), identifiée par le
/// couple (<see cref="SourceRegimeCode"/>, <see cref="Part"/>). Lève une erreur « introuvable » si
/// aucune règle ne correspond. La suppression repasse la table « NON VALIDÉE » (item TVA05 §2) et est
/// journalisée (append-only) de façon atomique. Le tenant est résolu par le contexte (CLAUDE.md n°9).
/// </summary>
public sealed record RemoveMappingRuleCommand : ICommand
{
    /// <summary>Code régime de la règle à supprimer.</summary>
    public required string SourceRegimeCode { get; init; }

    /// <summary>Part de la règle à supprimer.</summary>
    public required string Part { get; init; }
}
