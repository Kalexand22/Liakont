namespace Liakont.Host.Signatures;

using System.Collections.Generic;
using Liakont.Modules.DocumentApproval.Contracts.DTOs;

/// <summary>
/// Vue composite (lecture seule) de l'état de validation/signature d'un document pour une finalité, assemblée
/// par <see cref="ISignatureConsoleQueries"/> pour la page console des signatures (SIG10, F17 §0). DTO de
/// présentation pur : la tentative la PLUS RÉCENTE (<see cref="Latest"/>, <c>null</c> si aucune validation
/// n'existe) et le journal append-only des transitions (<see cref="Log"/>, du plus récent au plus ancien).
/// Aucune logique métier — la machine d'états fermée et le journal restent dans le module DocumentApproval
/// (ADR-0028) ; cette vue ne fait que les exposer à la page.
/// </summary>
public sealed record SignatureStatusView
{
    /// <summary>Tentative la plus récente (indépendamment de sa terminalité), ou <c>null</c> si aucune validation n'existe.</summary>
    public DocumentValidationDto? Latest { get; init; }

    /// <summary>Journal append-only des transitions (toutes tentatives), du plus récent au plus ancien.</summary>
    public IReadOnlyList<DocumentApprovalLogEntryDto> Log { get; init; } = [];
}
