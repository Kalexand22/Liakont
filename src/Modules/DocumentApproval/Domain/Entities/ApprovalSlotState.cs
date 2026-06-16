namespace Liakont.Modules.DocumentApproval.Domain.Entities;

/// <summary>
/// État d'un slot d'approbation N-parties (ADR-0028 §8). Un slot est identifié par son <c>SignerId</c> et
/// rempli de façon <b>idempotente</b> (une 2ᵉ preuve du même signataire ne change rien). Valeurs persistées
/// (int) — ordre figé.
/// </summary>
public enum ApprovalSlotState
{
    /// <summary>En attente de l'approbation de ce signataire / palier.</summary>
    Pending = 0,

    /// <summary>Approuvé par ce signataire (porte un niveau de preuve).</summary>
    Approved = 1,

    /// <summary>Refusé par ce signataire — bascule l'agrégat en terminal négatif IMMÉDIATEMENT (terminaison négative, §8).</summary>
    Rejected = 2,
}
