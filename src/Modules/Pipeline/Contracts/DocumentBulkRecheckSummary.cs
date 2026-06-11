namespace Liakont.Modules.Pipeline.Contracts;

/// <summary>
/// Synthèse d'une re-vérification EN MASSE (FIX207) : décompte par issue des documents re-vérifiés par
/// <see cref="IDocumentRecheckService.RecheckManyAsync"/>. La re-vérification de masse boucle la re-vérification
/// UNITAIRE (même source unique de la décision de blocage fiscal, même trace d'audit append-only par document —
/// CLAUDE.md n°2/3/4) et n'agrège QUE des compteurs (aucune règle fiscale ici). Tenant-scopée (le tenant est
/// résolu par la requête, comme la re-vérification unitaire).
/// </summary>
public sealed record DocumentBulkRecheckSummary
{
    /// <summary>Nombre de documents DISTINCTS effectivement re-vérifiés (= <see cref="Unblocked"/> + <see cref="StillBlocked"/> + <see cref="Unavailable"/> + <see cref="Skipped"/>).</summary>
    public required int Total { get; init; }

    /// <summary>Documents débloqués (<c>Blocked → ReadyToSend</c>).</summary>
    public required int Unblocked { get; init; }

    /// <summary>Documents restés bloqués (re-vérification tournée, aucune transition — un fait d'audit est tout de même inscrit, FIX02).</summary>
    public required int StillBlocked { get; init; }

    /// <summary>Documents dont le contenu pivot stagé est indisponible (pas encore stagé ou altéré) : impossible de re-vérifier, restent bloqués.</summary>
    public required int Unavailable { get; init; }

    /// <summary>Documents ignorés car l'état a déjà changé (introuvable dans le tenant, ou plus dans l'état <c>Blocked</c>) — aucun fait d'audit inscrit.</summary>
    public required int Skipped { get; init; }
}
