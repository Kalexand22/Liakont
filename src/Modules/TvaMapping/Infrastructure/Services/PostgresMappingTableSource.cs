namespace Liakont.Modules.TvaMapping.Infrastructure.Services;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.TvaMapping.Domain.Entities;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Implémentation de <see cref="IMappingTableSource"/> : charge et reconstitue la table de mapping du
/// tenant courant via <see cref="TvaMappingMaterializer"/> (re-validation structurelle au chargement).
/// Tenant-scopée par la connexion (database-per-tenant, CLAUDE.md n°9).
/// </summary>
internal sealed class PostgresMappingTableSource : IMappingTableSource
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresMappingTableSource(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<MappingTable?> LoadAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.OpenAsync(cancellationToken);
        return await TvaMappingMaterializer.LoadByCompanyAsync(conn, companyId, transaction: null, cancellationToken);
    }
}
