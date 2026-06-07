namespace Liakont.Modules.Supervision.Contracts;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Supervision.Contracts.DTOs;

/// <summary>
/// Lectures des alertes de supervision (consommées par le dashboard SUP02). Tenant-scopées PAR LA
/// CONNEXION (database-per-tenant, blueprint §7) : la surface du dashboard cross-tenant (lecture seule,
/// seul cas autorisé — blueprint §7 règle 2) agrège ces lectures tenant par tenant.
/// </summary>
public interface IAlertQueries
{
    /// <summary>Alertes ACTIVES (non résolues) du tenant courant, déclenchement le plus récent d'abord.</summary>
    Task<IReadOnlyList<AlertDto>> ListActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Les <paramref name="max"/> alertes les plus récentes (actives et résolues) du tenant courant.</summary>
    Task<IReadOnlyList<AlertDto>> ListRecentAsync(int max, CancellationToken cancellationToken = default);

    /// <summary>Alerte par identifiant (tenant courant), ou <c>null</c> si absente.</summary>
    Task<AlertDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
