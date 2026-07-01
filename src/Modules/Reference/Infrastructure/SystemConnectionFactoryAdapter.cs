namespace Liakont.Modules.Reference.Infrastructure;

using System.Data;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Adapte <see cref="ISystemConnectionFactory"/> (base système partagée) à <see cref="IConnectionFactory"/>
/// pour réutiliser <see cref="TransactionScope"/>. Le référentiel de correspondance pays (ADR-0038) est une
/// table CROSS-INSTANCE universelle (aucun tenant_id) : elle vit dans la base SYSTÈME, jamais dans une base
/// tenant.
/// </summary>
internal sealed class SystemConnectionFactoryAdapter : IConnectionFactory
{
    private readonly ISystemConnectionFactory _systemConnectionFactory;

    public SystemConnectionFactoryAdapter(ISystemConnectionFactory systemConnectionFactory)
    {
        _systemConnectionFactory = systemConnectionFactory;
    }

    public Task<IDbConnection> OpenAsync(CancellationToken cancellationToken = default) =>
        _systemConnectionFactory.OpenAsync(cancellationToken);
}
