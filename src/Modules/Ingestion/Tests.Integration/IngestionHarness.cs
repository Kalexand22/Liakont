namespace Liakont.Modules.Ingestion.Tests.Integration;

using Liakont.Modules.Ingestion.Application;
using Liakont.Modules.Ingestion.Contracts.Authentication;
using Liakont.Modules.Ingestion.Contracts.Queries;
using Liakont.Modules.Ingestion.Infrastructure;
using Liakont.Modules.Ingestion.Infrastructure.Handlers.Commands;
using Liakont.Modules.Ingestion.Infrastructure.Handlers.Queries;
using Liakont.Modules.Ingestion.Infrastructure.Queries;
using Liakont.Modules.Ingestion.Tests.Integration.Doubles;
using Liakont.Modules.Ingestion.Tests.Integration.Fixtures;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Assemble les vraies dépendances du module (UoW système, authentificateur, requêtes, fournisseur de
/// configuration) pour un tenant donné — isolation par test via un slug de tenant unique.
/// </summary>
internal sealed class IngestionHarness
{
    public IngestionHarness(IngestionDatabaseFixture fixture, string tenantId)
    {
        TenantId = tenantId;
        ConnectionFactory = fixture.CreateConnectionFactory();
        UowFactory = new PostgresAgentRegistryUnitOfWorkFactory(ConnectionFactory);
        Authenticator = new AgentAuthenticator(ConnectionFactory);
        Queries = new PostgresAgentQueries(ConnectionFactory);
        ConfigurationProvider = new SafeDefaultAgentConfigurationProvider();
        TenantContext = new TestTenantContext(tenantId);
    }

    public string TenantId { get; }

    public NpgsqlConnectionFactory ConnectionFactory { get; }

    public IAgentRegistryUnitOfWorkFactory UowFactory { get; }

    public IAgentAuthenticator Authenticator { get; }

    public IAgentQueries Queries { get; }

    public IAgentConfigurationProvider ConfigurationProvider { get; }

    public ITenantContext TenantContext { get; }

    public RegisterAgentHandler RegisterHandler => new(UowFactory, TenantContext);

    public RevokeAgentHandler RevokeHandler => new(UowFactory, TenantContext);

    public RotateAgentKeyHandler RotateHandler => new(UowFactory, TenantContext);

    public RecordHeartbeatHandler HeartbeatHandler => new(UowFactory, ConfigurationProvider);

    public GetAgentsHandler AgentsHandler => new(Queries, TenantContext);

    public GetAgentConfigurationHandler ConfigurationHandler => new(ConfigurationProvider);
}
