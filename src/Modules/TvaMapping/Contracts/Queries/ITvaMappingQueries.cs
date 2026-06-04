namespace Liakont.Modules.TvaMapping.Contracts.Queries;

using Liakont.Modules.TvaMapping.Contracts.DTOs;

/// <summary>
/// Lectures de la table de mapping TVA. Chaque méthode est scopée par <paramref name="companyId"/>
/// (résolu par le contexte appelant via <c>ICompanyFilter</c>) — jamais de lecture cross-tenant
/// (CLAUDE.md n°9/17). Une table persistée structurellement invalide lève une exception au
/// chargement (item TVA01 §4) plutôt que d'être servie fausse.
/// </summary>
public interface ITvaMappingQueries
{
    /// <summary>
    /// Retourne la table de mapping du tenant, ou <c>null</c> si aucune table n'est paramétrée.
    /// </summary>
    /// <param name="companyId">Tenant cible.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    Task<MappingTableDto?> GetMappingTable(Guid companyId, CancellationToken ct = default);
}
