namespace Liakont.Modules.DocumentApproval.Domain.Entities;

using Liakont.Modules.Signature.Contracts;

/// <summary>
/// Slot d'approbation N-parties (ADR-0028 §8). Modélise UN signataire / palier d'un ensemble FIXE défini à la
/// création. <b>Idempotent par <see cref="SignerId"/></b> : une 2ᵉ preuve du même signataire ne remplit rien
/// de plus (jamais un compteur). Porte son propre niveau de preuve — la Règle de gate (§5 cond. 2) s'évalue
/// PAR slot (« tous les slots ≥ niveau requis »).
/// </summary>
public sealed class ApprovalSlot
{
    private ApprovalSlot()
    {
    }

    /// <summary>Identifiant du signataire / palier (clé d'idempotence ; non vide).</summary>
    public string SignerId { get; private set; } = null!;

    /// <summary>État du slot (machine simple Pending → Approved / Rejected).</summary>
    public ApprovalSlotState State { get; private set; }

    /// <summary>Niveau de preuve attaché lors de l'approbation (None tant que non approuvé).</summary>
    public SignatureLevel ProofLevel { get; private set; }

    /// <summary>Référence de la preuve (rapatriée en WORM par le job de drain, SIG07) ; <c>null</c> tant qu'absente.</summary>
    public string? ProofId { get; private set; }

    /// <summary>Le slot est rempli (approuvé).</summary>
    public bool IsApproved => State == ApprovalSlotState.Approved;

    /// <summary>Crée un slot en attente pour un signataire (à la création de l'agrégat).</summary>
    public static ApprovalSlot CreatePending(string signerId)
    {
        if (string.IsNullOrWhiteSpace(signerId))
        {
            throw new ArgumentException("L'identifiant du signataire (SignerId) est obligatoire.", nameof(signerId));
        }

        return new ApprovalSlot
        {
            SignerId = signerId,
            State = ApprovalSlotState.Pending,
            ProofLevel = SignatureLevel.None,
            ProofId = null,
        };
    }

    /// <summary>Reconstitue un slot depuis la base (chemin de chargement) — sans rejouer la machine.</summary>
    public static ApprovalSlot Reconstitute(string signerId, ApprovalSlotState state, SignatureLevel proofLevel, string? proofId)
        => new()
        {
            SignerId = signerId,
            State = state,
            ProofLevel = proofLevel,
            ProofId = proofId,
        };

    internal void Approve(SignatureLevel proofLevel, string? proofId)
    {
        State = ApprovalSlotState.Approved;
        ProofLevel = proofLevel;
        ProofId = proofId;
    }

    internal void Reject() => State = ApprovalSlotState.Rejected;
}
