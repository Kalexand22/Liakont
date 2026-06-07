namespace Liakont.Modules.Pipeline.Application;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Pipeline.Domain.Payments;

/// <summary>
/// Écriture/lecture de la projection des agrégats jour×taux de paiement (<c>pipeline.payment_aggregations</c>,
/// PIP03a). PROJECTION RECALCULÉE INTÉGRALEMENT à chaque exécution : <see cref="ReplaceAllAsync"/> REMPLACE la
/// projection entière du tenant (purge + ré-écriture atomique), de sorte qu'aucune clé (jour, taux) périmée ne
/// survit si un encaissement est re-daté/retiré côté source. TENANT-SCOPÉ : la connexion EST le tenant
/// (database-per-tenant, blueprint §7) — aucun accès cross-tenant. NI table d'audit NI WORM (la piste d'audit
/// immuable des transmissions reste <c>payments.payment_aggregate_events</c>, écrite par PIP03b).
/// </summary>
public interface IPaymentAggregationStore
{
    /// <summary>
    /// Remplace ATOMIQUEMENT toute la projection du tenant par le jeu d'agrégats recomposé (purge des lignes
    /// existantes puis insertion). Un jeu vide vide la projection (plus aucun agrégat éligible).
    /// </summary>
    Task ReplaceAllAsync(IReadOnlyList<PaymentDailyAggregate> aggregates, CancellationToken cancellationToken = default);

    /// <summary>Tous les agrégats de paiement du tenant courant (lecture — console/tests).</summary>
    Task<IReadOnlyList<PaymentDailyAggregate>> GetAllAsync(CancellationToken cancellationToken = default);
}
