namespace Liakont.Modules.TvaMapping.Infrastructure.Services;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.TvaMapping.Domain.Entities;

/// <summary>
/// Charge la table de mapping (entité de domaine) du tenant courant. Seam interne TESTABLE : isole
/// l'orchestration de mapping (<see cref="TvaMappingService"/>, pure une fois la table connue) de l'accès
/// base (<see cref="PostgresMappingTableSource"/>).
/// </summary>
internal interface IMappingTableSource
{
    /// <summary>La table du tenant, ou <c>null</c> si aucune n'est définie (CFG/TVA non paramétré).</summary>
    Task<MappingTable?> LoadAsync(Guid companyId, CancellationToken cancellationToken = default);
}
