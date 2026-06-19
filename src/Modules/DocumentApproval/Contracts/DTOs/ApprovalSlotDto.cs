namespace Liakont.Modules.DocumentApproval.Contracts.DTOs;

/// <summary>
/// Vue de lecture (seule) d'un slot d'approbation N-parties (ADR-0028 §8). DTO pur. Le slot est identifié
/// par <see cref="SignerId"/> (idempotent : une 2ᵉ preuve du même signataire ne remplit rien de plus).
/// <see cref="ProofLevel"/> et <see cref="State"/> sont exposés sous forme de <b>nom</b> (pas l'enum de
/// domaine ni l'enum Signature) pour garder la surface <c>Contracts</c> sans dépendance.
/// </summary>
public sealed record ApprovalSlotDto
{
    /// <summary>Identifiant du signataire / palier (clé d'idempotence du slot).</summary>
    public required string SignerId { get; init; }

    /// <summary>État du slot (nom de <c>ApprovalSlotState</c> : Pending / Approved / Rejected).</summary>
    public required string State { get; init; }

    /// <summary>Niveau de preuve attaché au slot (nom de <c>SignatureLevel</c> : None / Recorded / SES / AES / QES).</summary>
    public required string ProofLevel { get; init; }

    /// <summary>Référence de la preuve (rapatriée en WORM par le job de drain, SIG07) ; <c>null</c> tant qu'absente.</summary>
    public string? ProofId { get; init; }
}
