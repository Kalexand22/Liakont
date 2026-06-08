namespace Liakont.Modules.TvaMapping.Contracts.Queries;

using System.Collections.Generic;
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

    /// <summary>
    /// Retourne le journal append-only des modifications de la table du tenant (item TVA05 §3), trié
    /// du plus récent au plus ancien — pour l'affichage de l'historique dans la console (API04/WEB07).
    /// Lecture seule : aucune écriture ne passe par cette surface (le journal est immuable, CLAUDE.md n°4).
    /// Liste vide si aucune modification n'a été journalisée pour ce tenant.
    /// </summary>
    /// <param name="companyId">Tenant cible.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    Task<IReadOnlyList<MappingChangeLogEntryDto>> GetChangeLog(Guid companyId, CancellationToken ct = default);
}
