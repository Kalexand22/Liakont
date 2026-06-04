namespace Liakont.Modules.TvaMapping.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Valide humainement la table de mapping TVA du tenant courant (workflow expert-comptable,
/// item TVA05 §4) : renseigne <see cref="ValidatedBy"/> et la date de validation (date courante). La
/// table passe « VALIDÉE », ce qui lève la suspension des envois en production (garde-fou PIP01). La
/// validation est journalisée (append-only) comme toute mutation. Le tenant est résolu par le contexte
/// (CLAUDE.md n°9).
/// </summary>
public sealed record ValidateMappingTableCommand : ICommand
{
    /// <summary>Identité du valideur (expert-comptable). Obligatoire.</summary>
    public required string ValidatedBy { get; init; }
}
