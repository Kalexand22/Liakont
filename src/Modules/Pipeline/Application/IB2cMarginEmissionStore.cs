namespace Liakont.Modules.Pipeline.Application;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Pipeline.Domain.B2cReporting;

/// <summary>
/// Journal d'émission e-reporting B2C agrégé (flux 10.3) — APPEND-ONLY, base DU TENANT (isolation par la
/// connexion). Porte l'anti-doublon côté produit (l'API SuperPDP n'expose aucune clé d'idempotence) au grain
/// DOCUMENT (décision Karl D3 : attempt-once). AUCUN chemin d'update/delete (CLAUDE.md n°4).
/// <para>
/// <b>PARTAGÉ par les DEUX régimes B2C</b> (depuis BUG-8) : la MARGE (<c>TMA1</c>) et le PRIX TOTAL taxable
/// (<c>TLB1</c>) écrivent dans CE même journal — la colonne <see cref="B2cMarginEmissionEntry.Category"/>
/// discrimine le régime, et l'attempt-once est volontairement commun (un document est soit marge soit taxable,
/// jamais les deux). Le nom historique « Margin » est conservé pour ne pas faire churner le store/console/migration
/// validés ; renommage en « B2cEmission » = dette de clarté différée (non-correctness), pas un faux-vert.
/// </para>
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
