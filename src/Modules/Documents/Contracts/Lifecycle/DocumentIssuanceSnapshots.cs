namespace Liakont.Modules.Documents.Contracts.Lifecycle;

/// <summary>
/// Triplet de preuve d'un document ÉMIS, porté à la frontière Contracts (F06 §3 / TRK04) : payload pivot
/// transmis, réponse brute de la Plateforme Agréée, et trace de mapping TVA appliquée. Fournis par
/// l'appelant (pipeline, PIP01c) ; leur obligation (chaînes non vides) est vérifiée par le domaine à
/// l'émission (jamais d'émission sans preuve complète).
/// </summary>
public sealed record DocumentIssuanceSnapshots
{
    /// <summary>Snapshot du payload pivot exact transmis à la Plateforme Agréée (JSON).</summary>
    public required string PayloadSnapshot { get; init; }

    /// <summary>Réponse brute de la Plateforme Agréée, avec les identifiants DGFiP (JSON).</summary>
    public required string PaResponseSnapshot { get; init; }

    /// <summary>Trace de la/des règle(s) de mapping TVA appliquée(s) et de leur version (JSON, F03).</summary>
    public required string MappingTrace { get; init; }

    /// <summary>
    /// Identifiant du document attribué par la Plateforme Agréée à l'émission (clé de récupération aval :
    /// facture générée, tax reports — SYNC/PIP01d), ou <c>null</c>. Son absence n'altère pas une référence déjà
    /// posée (jamais un effacement). Ce n'est PAS une preuve d'audit (les trois snapshots le sont).
    /// </summary>
    public string? PaDocumentId { get; init; }
}
