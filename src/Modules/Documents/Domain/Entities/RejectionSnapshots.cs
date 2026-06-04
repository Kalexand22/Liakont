namespace Liakont.Modules.Documents.Domain.Entities;

/// <summary>
/// Snapshots de preuve d'un document REJETÉ par la Plateforme Agréée (F06 §3, item TRK04) : la tentative
/// ratée fait partie de la piste d'audit — un contrôle fiscal peut exiger de prouver ce qui a été TENTÉ,
/// pas seulement ce qui a réussi. On archive le <see cref="PayloadSnapshot"/> envoyé et la
/// <see cref="PaResponseSnapshot"/> brute de rejet (motif PA). Les deux sont OBLIGATOIRES ; la trace de
/// mapping n'est pas requise pour un rejet (le document n'a pas été émis). Portés par le
/// <see cref="DocumentEvent"/> de rejet (colonnes jsonb append-only).
/// </summary>
public sealed class RejectionSnapshots
{
    /// <summary>Construit les snapshots d'un rejet. Chaque snapshot est un document JSON non vide (F06 §3).</summary>
    public RejectionSnapshots(string payloadSnapshot, string paResponseSnapshot)
    {
        PayloadSnapshot = IssuanceSnapshots.RequireSnapshot(
            payloadSnapshot,
            nameof(payloadSnapshot),
            "Le snapshot du payload pivot transmis est obligatoire pour un document rejeté (preuve de la tentative, F06 §3).");
        PaResponseSnapshot = IssuanceSnapshots.RequireSnapshot(
            paResponseSnapshot,
            nameof(paResponseSnapshot),
            "Le snapshot de la réponse de rejet brute de la Plateforme Agréée est obligatoire pour un document rejeté (F06 §3).");
    }

    /// <summary>Snapshot du payload pivot exact transmis à la Plateforme Agréée (JSON).</summary>
    public string PayloadSnapshot { get; }

    /// <summary>Réponse brute de rejet de la Plateforme Agréée, avec le motif (JSON).</summary>
    public string PaResponseSnapshot { get; }
}
