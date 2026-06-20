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
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.Database;
using Stratum.Common.Infrastructure.Outbox;

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

        // Réception (PIV04) : vrai outbox writer (écrit dans outbox.pending_events de la même base),
        // registre de réception, régimes source, et espion du port de création de document.
        var outboxWriter = new OutboxWriter(NullLogger<OutboxWriter>.Instance);
        ReceivedDocumentUowFactory = new PostgresReceivedDocumentUnitOfWorkFactory(ConnectionFactory, outboxWriter);
        SourceTaxRegimeWriter = new PostgresSourceTaxRegimeWriter(ConnectionFactory);
        SourceTaxRegimeQueries = new PostgresSourceTaxRegimeQueries(ConnectionFactory);
        ExtractorCapabilitiesWriter = new PostgresExtractorCapabilitiesWriter(ConnectionFactory);
        ExtractorCapabilitiesQueries = new PostgresExtractorCapabilitiesQueries(ConnectionFactory);
        DocumentIntake = new RecordingDocumentIntake();
        PayloadStagingStore = new RecordingPayloadStagingStore();
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

    public IReceivedDocumentUnitOfWorkFactory ReceivedDocumentUowFactory { get; }

    public ISourceTaxRegimeWriter SourceTaxRegimeWriter { get; }

    public ISourceTaxRegimeQueries SourceTaxRegimeQueries { get; }

    public IExtractorCapabilitiesWriter ExtractorCapabilitiesWriter { get; }

    public IExtractorCapabilitiesQueries ExtractorCapabilitiesQueries { get; }

    public RecordingDocumentIntake DocumentIntake { get; }

    public RecordingPayloadStagingStore PayloadStagingStore { get; }

    public IngestDocumentBatchHandler BatchHandler =>
        new(ReceivedDocumentUowFactory, SourceTaxRegimeWriter, ExtractorCapabilitiesWriter, DocumentIntake, PayloadStagingStore, NullLogger<IngestDocumentBatchHandler>.Instance);
}
