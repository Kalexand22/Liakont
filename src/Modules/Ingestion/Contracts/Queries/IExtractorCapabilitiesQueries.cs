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

    /// <summary>
    /// Indique si AU MOINS un agent du tenant a déclaré exposer les encaissements
    /// (<c>ExposesPayments = true</c>). Sert au pipeline F09 (RD403) à distinguer une source qui
    /// <b>n'expose pas</b> les paiements (aucune déclaration <c>true</c> → e-reporting de paiement non
    /// applicable, on ne transmet jamais un néant à tort) d'une source qui les expose mais n'a aucun
    /// encaissement sur la période (« zéro encaissement »). Renvoie <c>false</c> si aucune capacité n'a
    /// jamais été déclarée (add-only : agent N-1 / source muette) — par défaut SÛR, on ne présume pas
    /// que toute source expose les paiements (ADR-0004 D2 : flux 10.4 conditionné à la capacité, pas
    /// présumé). Tenant-scopé, jamais de lecture cross-tenant.
    /// </summary>
    Task<bool> AnyAgentExposesPaymentsAsync(
        string tenantId,
        CancellationToken cancellationToken = default);
}
