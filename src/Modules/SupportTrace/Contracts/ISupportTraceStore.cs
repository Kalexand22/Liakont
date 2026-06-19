namespace Liakont.Modules.SupportTrace.Contracts;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Magasin de la TRACE DE SUPPORT du Factur-X réellement transmis (FX06, F16 §7) — une COPIE du document
/// transmis, conservée à des fins de SUPPORT (rejouer/diagnostiquer un envoi). Store DÉDIÉ, distinct par
/// nature :
/// <list type="bullet">
/// <item>de la PISTE D'AUDIT WORM (<c>documents.document_events</c>, append-only, jamais purgée) ;</item>
/// <item>de l'ARCHIVE PROBANTE (coffre WORM, NF Z42-013, rétention 10 ans — Pilotage).</item>
/// </list>
/// NON-WORM / <b>purgeable</b> à RÉTENTION COURTE (proposition 90 jours configurable, F16 §10) : la purge
/// planifiée est AUTORISÉE précisément parce que ce n'est PAS de l'audit (CLAUDE.md n°4 inchangé). La copie
/// est <b>tenant-scopée</b> par construction et <b>chiffrée au repos</b> — données fiscales protégées
/// (CLAUDE.md n°9/10). Le store n'a AUCUNE connaissance de <c>documents.document_events</c> ni du coffre
/// d'archive : la purge ne peut donc PAS les altérer (garde de frontière vérifiée par test).
/// </summary>
public interface ISupportTraceStore
{
    /// <summary>
    /// Écrit (durablement) la copie de l'artefact Factur-X transmis pour un document, chiffrée au repos et
    /// tenant-scopée. <paramref name="recordedAtUtc"/> est l'horodatage de transmission : il porte la
    /// RÉTENTION (la purge supprime les entrées plus anciennes que la fenêtre). Idempotent sur
    /// <c>(tenant, document, jour)</c> : ré-écrire remplace proprement (filet de sécurité au renvoi).
    /// </summary>
    /// <param name="tenantId">Le tenant propriétaire (jamais cross-tenant).</param>
    /// <param name="documentId">Le document dont l'artefact a été transmis.</param>
    /// <param name="facturX">Les octets du Factur-X transmis (PDF/A-3 scellé).</param>
    /// <param name="recordedAtUtc">L'horodatage de transmission (porte la rétention).</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task WriteAsync(
        string tenantId,
        Guid documentId,
        ReadOnlyMemory<byte> facturX,
        DateTimeOffset recordedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Relit la copie de support la PLUS RÉCENTE conservée pour un document (déchiffrée), ou <c>null</c> si
    /// aucune n'existe (jamais écrite, ou déjà purgée par rétention). Lecture tenant-scopée.
    /// </summary>
    /// <param name="tenantId">Le tenant propriétaire.</param>
    /// <param name="documentId">Le document recherché.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <returns>Les octets du Factur-X conservé, ou <c>null</c>.</returns>
    Task<byte[]?> ReadAsync(string tenantId, Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Purge (supprime) les traces de support du tenant dont l'horodatage de transmission est STRICTEMENT
    /// antérieur à <paramref name="cutoffUtc"/>. Purge LÉGITIME : store transitoire de support, NI table
    /// d'audit NI coffre WORM (CLAUDE.md n°4/12 inchangés). Idempotente (no-op si rien d'expiré). Bornée au
    /// périmètre du tenant : ne touche jamais un autre tenant, ni la piste d'audit, ni l'archive probante.
    /// </summary>
    /// <param name="tenantId">Le tenant dont on purge les traces expirées.</param>
    /// <param name="cutoffUtc">La borne de rétention : tout ce qui est plus ancien est supprimé.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <returns>Le nombre d'entrées (jours) purgées.</returns>
    Task<int> PurgeOlderThanAsync(string tenantId, DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default);
}
