namespace Liakont.Modules.Signature.Contracts;

/// <summary>
/// État relu d'une demande de signature côté fournisseur (ADR-0027 §2). Retourné par
/// <see cref="ISignatureProvider.GetSignatureStatusAsync"/> — utilisé par un job de réconciliation
/// quand le fournisseur déclare <see cref="CompletionTransport.Polling"/>, ou pour relire un état après
/// un webhook.
/// </summary>
public sealed record SignatureStatus
{
    /// <summary>Référence côté fournisseur dont l'état est relu.</summary>
    public required string ProviderReference { get; init; }

    /// <summary>État de complétion courant.</summary>
    public required SignatureCompletionState State { get; init; }

    /// <summary>Niveau de preuve effectivement atteint si la signature est complétée, sinon <c>null</c>.</summary>
    public SignatureLevel? AchievedLevel { get; init; }

    /// <summary>Horodatage UTC de complétion, ou <c>null</c> si non complétée.</summary>
    public DateTimeOffset? CompletedAtUtc { get; init; }
}
