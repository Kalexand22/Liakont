namespace Liakont.Modules.Ingestion.Infrastructure;

using System.Data;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Adapte <see cref="ISystemConnectionFactory"/> (base système partagée) à <see cref="IConnectionFactory"/>
/// pour réutiliser <see cref="TransactionScope"/>. Le registre d'agents et l'historique des heartbeats
/// vivent dans la base SYSTÈME (pas dans une base tenant) : la résolution d'une clé API vers son
/// tenant précède tout contexte tenant (F12 §3.1).
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
