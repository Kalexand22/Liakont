namespace Liakont.Modules.Pipeline.Application;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Pipeline.Domain.B2cReporting;

/// <summary>
/// Écriture du registre de la marge à déclarer (Livrable 2, <c>pipeline.margin_registry</c>). Écrit au CHECK
/// (PIP01b) et au re-CHECK (API02b) quand un document est résolu au régime de la marge, lu par la page console
/// « TVA / Déclaration ». TENANT-SCOPÉ : la connexion EST le tenant (database-per-tenant, blueprint §7) — aucun
/// accès cross-tenant. PROJECTION recalculable (≠ <see cref="IB2cMarginEmissionStore"/>, WORM) : l'écriture est
/// un UPSERT sur <c>document_id</c> (un doc = un taux), et un document qui n'est plus au régime de la marge au
/// re-CHECK est SUPPRIMÉ — le registre suit toujours l'état courant des documents.
/// </summary>
public interface IMarginRegistryStore
{
    /// <summary>
    /// Insère ou met à jour (UPSERT sur <c>document_id</c>) l'entrée de marge du document. Idempotent : un
    /// re-CHECK du même document à la même marge ré-écrit la même valeur (projection, pas de doublon).
    /// </summary>
    Task UpsertAsync(MarginRegistryEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Supprime l'entrée du document si elle existe (sans effet sinon) : appelé quand un document prêt à l'envoi
    /// n'est plus au régime de la marge au re-CHECK (re-mapping → taxable), pour ne pas laisser une marge périmée.
    /// </summary>
    Task DeleteAsync(Guid documentId, CancellationToken cancellationToken = default);
}
