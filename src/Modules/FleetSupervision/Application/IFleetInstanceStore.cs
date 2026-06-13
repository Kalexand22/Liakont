namespace Liakont.Modules.FleetSupervision.Application;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.FleetSupervision.Contracts.DTOs;
using Liakont.Modules.FleetSupervision.Domain;

/// <summary>
/// Persistance de l'état de flotte côté CENTRAL (OPS04), dans la base SYSTÈME de l'instance mutualisée.
/// Upsert idempotent par instance (préserve <c>first_seen_utc</c> et la version notifiée), lecture du parc
/// pour le dashboard, et primitives de la passe de notification de mise à jour.
/// </summary>
public interface IFleetInstanceStore
{
    /// <summary>Insère ou met à jour l'état d'une instance (préserve premier-vu + version notifiée).</summary>
    Task UpsertAsync(FleetInstance instance, CancellationToken cancellationToken = default);

    /// <summary>Liste tout le parc connu (dashboard de flotte).</summary>
    Task<IReadOnlyList<FleetInstanceDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Liste les instances self-hosted joignables par email (passe de notification de version).</summary>
    Task<IReadOnlyList<FleetNotificationCandidate>> ListNotificationCandidatesAsync(CancellationToken cancellationToken = default);

    /// <summary>Mémorise la version pour laquelle l'instance vient d'être notifiée (anti-rebond).</summary>
    Task MarkNotifiedAsync(string instanceId, string notifiedVersion, CancellationToken cancellationToken = default);
}
