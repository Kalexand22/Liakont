namespace Liakont.Modules.Documents.Contracts.Lifecycle;

/// <summary>
/// Snapshots de preuve d'un document REJETÉ par la Plateforme Agréée, à la frontière Contracts
/// (F06 §3 / TRK04) : payload transmis + réponse de rejet brute. La trace de mapping n'est pas requise
/// (le document n'a pas été émis). Fournis par l'appelant (pipeline, PIP01c).
/// </summary>
public sealed record DocumentRejectionSnapshots
{
    /// <summary>Snapshot du payload pivot exact transmis à la Plateforme Agréée (JSON).</summary>
    public required string PayloadSnapshot { get; init; }

    /// <summary>Réponse brute de rejet de la Plateforme Agréée, avec le motif (JSON).</summary>
    public required string PaResponseSnapshot { get; init; }
}
