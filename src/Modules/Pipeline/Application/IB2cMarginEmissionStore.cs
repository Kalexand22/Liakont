namespace Liakont.Modules.Pipeline.Application;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Pipeline.Domain.B2cReporting;

/// <summary>
/// Journal d'émission e-reporting B2C de la marge (flux 10.3) — APPEND-ONLY, base DU TENANT (isolation par la
/// connexion). Porte l'anti-doublon côté produit (l'API SuperPDP n'expose aucune clé d'idempotence) au grain
/// DOCUMENT (décision Karl D3 : attempt-once). AUCUN chemin d'update/delete (CLAUDE.md n°4).
/// </summary>
public interface IB2cMarginEmissionStore
{
    /// <summary>
    /// Identifiants des documents marge DÉJÀ TENTÉS (toute entrée, quel que soit le statut, y compris
    /// <see cref="B2cMarginEmissionStatus.Pending"/>) : ces documents sont EXCLUS d'une nouvelle tentative
    /// automatique (attempt-once — jamais 2 POST sur une API sans dédoublonnage).
    /// </summary>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task<IReadOnlySet<Guid>> GetHandledDocumentIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>Ajoute une entrée au journal (append-only, jamais d'update/delete — CLAUDE.md n°4).</summary>
    /// <param name="entry">L'entrée d'émission à journaliser.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task AppendAsync(B2cMarginEmissionEntry entry, CancellationToken cancellationToken = default);
}
