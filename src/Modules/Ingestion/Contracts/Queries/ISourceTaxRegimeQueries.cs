namespace Liakont.Modules.Ingestion.Contracts.Queries;

using Liakont.Modules.Ingestion.Contracts.DTOs;

/// <summary>
/// Lectures des régimes de TVA source observés, scopées par tenant (base système, schéma
/// <c>ingestion</c>). Consommé par la détection de couverture TVA (TVA03) pour confronter les régimes
/// observés à la table de mapping du tenant. Jamais de lecture cross-tenant.
/// </summary>
public interface ISourceTaxRegimeQueries
{
    Task<IReadOnlyList<SourceTaxRegimeSummaryDto>> ListByTenantAsync(
        string tenantId,
        CancellationToken cancellationToken = default);
}
