namespace Liakont.Modules.Signature.Contracts;

/// <summary>
/// État de complétion d'une demande de signature relu via
/// <see cref="ISignatureProvider.GetSignatureStatusAsync"/> (ADR-0027 §2). Décrit le cycle de vie
/// CÔTÉ FOURNISSEUR ; la machine de validation du document (états métier, gate d'ouverture) est portée
/// par le module générique DocumentApproval (ADR-0028, SIG04), pas ici.
/// </summary>
public enum SignatureCompletionState
{
    /// <summary>État inconnu / référence non reconnue par le fournisseur.</summary>
    Unknown,

    /// <summary>Signature EN COURS (en attente du / des signataire(s)).</summary>
    Pending,

    /// <summary>Signature COMPLÉTÉE par le(s) signataire(s).</summary>
    Completed,

    /// <summary>Signature REFUSÉE par un signataire.</summary>
    Declined,

    /// <summary>Demande EXPIRÉE sans complétion.</summary>
    Expired,
}
