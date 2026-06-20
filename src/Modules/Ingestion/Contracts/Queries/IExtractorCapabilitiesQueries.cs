namespace Liakont.Modules.Ingestion.Contracts.Queries;

using Liakont.Modules.Ingestion.Contracts.DTOs;

/// <summary>
/// Lectures des capacités déclarées de la source d'un agent (ADR-0004 D2, RD401), scopées par tenant
/// (base système, schéma <c>ingestion</c>). Consommé par les adaptations métier à valeur présente
/// (RD403 : <c>ExposesPayments</c> pour F09, <c>IsMutableAfterIssue</c> pour l'alerte d'altération) et
/// les différés tracés (RD409). Jamais de lecture cross-tenant.
/// </summary>
public interface IExtractorCapabilitiesQueries
{
    /// <summary>
    /// Restitue les capacités déclarées par l'agent donné pour ce tenant, ou <c>null</c> si l'agent
    /// n'en a jamais transmis (agent N-1 / source qui ne déclare rien).
    /// </summary>
    Task<ExtractorCapabilitiesSummaryDto?> GetByAgentAsync(
        string tenantId,
        Guid agentId,
        CancellationToken cancellationToken = default);
}
