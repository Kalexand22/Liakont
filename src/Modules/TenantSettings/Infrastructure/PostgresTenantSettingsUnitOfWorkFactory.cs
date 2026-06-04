namespace Liakont.Modules.TenantSettings.Infrastructure;

using Liakont.Modules.TenantSettings.Application;
using Stratum.Common.Infrastructure.Database;

internal sealed class PostgresTenantSettingsUnitOfWorkFactory : ITenantSettingsUnitOfWorkFactory
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresTenantSettingsUnitOfWorkFactory(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ITenantSettingsUnitOfWork> BeginAsync(CancellationToken ct = default)
    {
        return await PostgresTenantSettingsUnitOfWork.BeginAsync(_connectionFactory, ct);
    }
}
