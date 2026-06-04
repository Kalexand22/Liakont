namespace Liakont.Modules.TvaMapping.Contracts.Queries;

using Liakont.Modules.TvaMapping.Contracts.DTOs;
using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Calcule le rapport de couverture du mapping TVA du tenant courant (item TVA03, F03 §4.3) :
/// croise les régimes source observés (push de l'agent — PIV04) avec la table de mapping du tenant.
/// Tenant-scopée par le contexte appelant (slug via <c>ITenantContext</c> pour les régimes observés,
/// <c>company_id</c> via <c>ICompanyFilter</c> pour la table) — jamais de lecture cross-tenant
/// (CLAUDE.md n°9/17). Recalculée à la demande : toujours à jour après chaque push d'agent et chaque
/// modification de la table (F03 §4.3).
/// </summary>
public sealed record GetMappingCoverageReportQuery : IQuery<MappingCoverageReportDto>;
