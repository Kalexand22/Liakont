namespace Liakont.Modules.Pipeline.Application;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Pipeline.Domain.Payments;

/// <summary>
/// Écriture/lecture de la projection des agrégats jour×taux de paiement (<c>pipeline.payment_aggregations</c>,
/// PIP03a). PROJECTION RECALCULÉE : <see cref="UpsertAsync"/> est idempotente sur (jour, taux). TENANT-SCOPÉ :
/// la connexion EST le tenant (database-per-tenant, blueprint §7) — aucun accès cross-tenant.
/// </summary>
public interface IPaymentAggregationStore
{
    /// <summary>Insère ou met à jour les agrégats jour×taux (upsert sur (date, taux)) pour le tenant courant.</summary>
    Task UpsertAsync(IReadOnlyList<PaymentDailyAggregate> aggregates, CancellationToken cancellationToken = default);

    /// <summary>Tous les agrégats de paiement du tenant courant (lecture — console/tests).</summary>
    Task<IReadOnlyList<PaymentDailyAggregate>> GetAllAsync(CancellationToken cancellationToken = default);
}
