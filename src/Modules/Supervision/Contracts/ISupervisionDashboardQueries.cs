namespace Liakont.Modules.Supervision.Contracts;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Supervision.Contracts.DTOs;

/// <summary>
/// Lectures AGRÉGÉES cross-tenant du dashboard de supervision (SUP02), réservées à l'opérateur d'instance
/// (permission <c>liakont.supervision</c>). C'est l'UNIQUE surface cross-tenant en lecture du produit
/// (CLAUDE.md n°9, blueprint §7 règle 2) : l'implémentation énumère les tenants (registre système) et
/// agrège, tenant par tenant, des lectures elles-mêmes tenant-scopées (<see cref="IAlertQueries"/>,
/// Documents, Ingestion). Aucune écriture sur la base d'un tenant hormis l'acquittement d'une alerte
/// (marqueur de prise en charge), scopé au tenant concerné.
/// </summary>
public interface ISupervisionDashboardQueries
{
    /// <summary>Vue d'ensemble : une ligne par tenant actif (alertes, état des agents, compteurs de documents).</summary>
    Task<IReadOnlyList<TenantSupervisionRowDto>> GetInstanceOverviewAsync(CancellationToken cancellationToken = default);

    /// <summary>Détail d'un tenant, ou <c>null</c> si le tenant est inconnu ou inactif.</summary>
    Task<TenantSupervisionDetailDto?> GetTenantDetailAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquitte l'alerte <paramref name="alertId"/> du tenant <paramref name="tenantId"/> au nom de
    /// <paramref name="operatorIdentity"/> (journalisé). Retourne <c>true</c> si l'alerte existait dans ce
    /// tenant et a été acquittée, <c>false</c> sinon. L'acquittement ne résout pas l'alerte.
    /// </summary>
    Task<bool> AcknowledgeAsync(string tenantId, Guid alertId, string operatorIdentity, CancellationToken cancellationToken = default);
}
