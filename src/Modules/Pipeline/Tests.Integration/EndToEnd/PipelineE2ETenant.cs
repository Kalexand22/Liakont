namespace Liakont.Modules.Pipeline.Tests.Integration.EndToEnd;

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using DbUp;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Serialization;
using Liakont.Agent.Contracts.Transport;
using Liakont.Modules.Archive.Domain;
using Liakont.Modules.Archive.Infrastructure;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Documents.Infrastructure;
using Liakont.Modules.Ingestion.Contracts;
using Liakont.Modules.Ingestion.Contracts.Commands;
using Liakont.Modules.Ingestion.Infrastructure;
using Liakont.Modules.Pipeline.Infrastructure;
using Liakont.Modules.Pipeline.Infrastructure.Check;
using Liakont.Modules.Pipeline.Infrastructure.Send;
using Liakont.Modules.Pipeline.Infrastructure.Sync;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Staging.Contracts;
using Liakont.Modules.Staging.Infrastructure;
using Liakont.Modules.TenantSettings.Infrastructure;
using Liakont.Modules.TvaMapping.Application;
using Liakont.Modules.TvaMapping.Domain.Entities;
using Liakont.Modules.TvaMapping.Infrastructure;
using Liakont.Modules.Transmission.Contracts;
using Liakont.Modules.Validation.Infrastructure;
using Liakont.PaClients.Fake;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Stratum.Common.Abstractions.Events;
using Stratum.Common.Abstractions.Jobs;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.Database;
using Stratum.Common.Infrastructure.Outbox;
using Testcontainers.PostgreSql;
using Xunit;
using VatCategory = Liakont.Agent.Contracts.Pivot.VatCategory;

/// <summary>
/// Un tenant de bout en bout du pipeline (PIP01d) sur sa PROPRE base PostgreSQL réelle (Testcontainers — une
/// base PAR tenant) : tous les modules consommés câblés avec leurs VRAIS services (ingestion réelle, staging,
/// mapping, validation, cycle de vie, archive WORM réelle, purge subordonnée au WORM), un plug-in PA factice
/// résolu via <see cref="IPaClientRegistry"/>. Exécute la chaîne complète <c>ingestion → CHECK → SEND → SYNC →
/// archive</c> par appels directs aux maillons (l'orchestration outbox/cron est portée par le Host).
/// </summary>
public sealed class PipelineE2ETenant : IAsyncLifetime
{
    private const string MappingVersion = "cmp-v1";
    private const string ProfileSiren = "404833048";
    private const string E2ETaxReportId = "TR-E2E";

    private static readonly string[] E2ETaxReportIds = [E2ETaxReportId];

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private NpgsqlConnectionFactory _connectionFactory = null!;
    private ServiceProvider? _provider;
    private string? _stagingRoot;
    private string? _archiveRoot;

    public PipelineE2ETenant(string slug)
    {
        Slug = slug;
    }

    /// <summary>Slug du tenant (sa base unique l'ignore — la base EST le tenant).</summary>
    public string Slug { get; }

    /// <summary>Identité de l'unique société du tenant.</summary>
    public Guid CompanyId { get; } = Guid.NewGuid();

    /// <summary>Plug-in PA factice du tenant (configuré publié, capacités complètes).</summary>
    public FakePaClient PaClient { get; private set; } = new();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var connectionString = _container.GetConnectionString();

        RunCommonMigrations(connectionString);
        RunModuleMigrations(connectionString, typeof(DocumentsModuleRegistration).Assembly);
        RunModuleMigrations(connectionString, typeof(TvaMappingModuleRegistration).Assembly);
        RunModuleMigrations(connectionString, typeof(TenantSettingsModuleRegistration).Assembly);
        RunModuleMigrations(connectionString, typeof(StagingModuleRegistration).Assembly);
        RunModuleMigrations(connectionString, typeof(PipelineModuleRegistration).Assembly);
        RunModuleMigrations(connectionString, typeof(ArchiveModuleRegistration).Assembly);
        RunModuleMigrations(connectionString, typeof(IngestionModuleRegistration).Assembly);

        _provider = BuildProvider(connectionString);
        await ConfigurePaClientAsync();
        await SeedTenantProfileAsync(connectionString);
        await SeedValidatedMappingTableAsync();
        await SeedActivePaAccountAsync(connectionString);
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }

        DeleteDirectory(_stagingRoot);
        DeleteDirectory(_archiveRoot);
        await _container.DisposeAsync();
    }

    /// <summary>Ingestion RÉELLE d'un document pivot (chemin de production : réception + staging + Detected + outbox).</summary>
    public async Task<DocumentPushStatus> IngestAsync(PivotDocumentDto pivot)
    {
        await using var scope = _provider!.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var response = await sender.Send(
            new IngestDocumentBatchCommand
            {
                AgentId = Guid.NewGuid(),
                TenantId = Slug,
                ContractVersion = "1",
                Documents = new[] { pivot },
                SourceTaxRegimes = Array.Empty<SourceTaxRegimeDto>(),
            },
            CancellationToken.None);
        return response.Results[0].Status;
    }

    /// <summary>Identifiant du document rangé pour la clé (référence source, empreinte), ou <c>null</c>.</summary>
    public async Task<Guid?> ResolveDocumentIdAsync(string sourceReference, string payloadHash)
    {
        await using var scope = _provider!.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredService<IDocumentQueries>();
        var status = await queries.FindStatusBySourceReferenceAndPayloadHashAsync(sourceReference, payloadHash);
        return status?.Id;
    }

    /// <summary>CHECK : consomme l'événement DocumentReceived → Detected → ReadyToSend / Blocked.</summary>
    public async Task RunCheckAsync(Guid documentId, string sourceReference, string payloadHash)
    {
        var consumer = new DocumentReceivedConsumer(
            new ProviderTenantScopeFactory(_provider!),
            NullLogger<DocumentReceivedConsumer>.Instance);
        await consumer.HandleAsync(BuildReceivedEvent(documentId, sourceReference, payloadHash));
    }

    /// <summary>SEND : émission via le plug-in PA factice → archive WORM → purge du staging subordonnée au WORM.</summary>
    public Task RunSendAsync() => RunTenantJobAsync(new SendTenantJob(PipelineRunTrigger.Scheduled));

    /// <summary>SYNC : réconciliation (facture PA + tax reports → addenda WORM) selon les capacités.</summary>
    public Task RunSyncAsync() => RunTenantJobAsync(new SyncTenantJob(PipelineRunTrigger.Scheduled));

    /// <summary>État courant d'un document (ou <c>null</c>).</summary>
    public async Task<string?> GetDocumentStateAsync(Guid documentId)
    {
        await using var scope = _provider!.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredService<IDocumentQueries>();
        var document = await queries.GetByIdAsync(documentId);
        return document?.State;
    }

    /// <summary>Piste d'audit append-only d'un document (pour vérifier la persistance des motifs de blocage).</summary>
    public async Task<IReadOnlyList<Liakont.Modules.Documents.Contracts.DTOs.DocumentEventDto>> GetEventsAsync(Guid documentId)
    {
        await using var scope = _provider!.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredService<IDocumentQueries>();
        return await queries.GetEventsAsync(documentId);
    }

    /// <summary>Vrai si un document existe (toute base : isolation tenant) avec cet identifiant.</summary>
    public async Task<bool> DocumentExistsAsync(Guid documentId)
    {
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM documents.documents WHERE id = @Id)",
            new { Id = documentId });
    }

    /// <summary>Nombre d'entrées de coffre (paquet initial + addenda) scellées pour un document.</summary>
    public async Task<int> ArchiveEntryCountAsync(Guid documentId)
    {
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM documents.archive_entries WHERE document_id = @Id",
            new { Id = documentId });
    }

    /// <summary>Indique si le contenu pivot est encore présent dans le staging.</summary>
    public async Task<bool> IsStagedAsync(Guid documentId, string payloadHash)
    {
        await using var scope = _provider!.CreateAsyncScope();
        var staging = scope.ServiceProvider.GetRequiredService<IPayloadStagingStore>();
        return await staging.ExistsAsync(new StagedPayloadKey(Slug, documentId, payloadHash));
    }

    private static void RunCommonMigrations(string connectionString)
    {
        var options = Options.Create(new DatabaseOptions { ConnectionString = connectionString });
        var migrationOptions = Options.Create(new MigrationAssembliesOptions());
        var runner = new MigrationRunner(options, migrationOptions, NullLogger<MigrationRunner>.Instance);
        runner.MigrateUp();
    }

    private static void RunModuleMigrations(string connectionString, Assembly moduleAssembly)
    {
        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(moduleAssembly, s => s.Contains(".Migrations.", StringComparison.Ordinal))
            .JournalToPostgresqlTable("outbox", "schema_versions")
            .WithTransactionPerScript()
            .LogToNowhere()
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
        {
            throw result.Error;
        }
    }

    private static void DeleteDirectory(string? path)
    {
        if (path is null || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private async Task RunTenantJobAsync(ITenantJob job)
    {
        await using var scope = _provider!.CreateAsyncScope();
        await job.ExecuteAsync(new TenantJobContext(Slug, scope.ServiceProvider));
    }

    private IntegrationEvent<Modules.Ingestion.Contracts.Events.DocumentReceivedV1> BuildReceivedEvent(
        Guid documentId,
        string sourceReference,
        string payloadHash) =>
        new()
        {
            EventId = Guid.NewGuid(),
            EventType = "ingestion.document.received",
            OccurredAt = DateTimeOffset.UtcNow,
            CorrelationId = documentId,
            ModuleSource = "Ingestion",
            Version = 1,
            Payload = new Modules.Ingestion.Contracts.Events.DocumentReceivedV1
            {
                TenantId = Slug,
                DocumentId = documentId,
                SourceReference = sourceReference,
                PayloadHash = payloadHash,
                ReceivedAtUtc = DateTimeOffset.UtcNow,
            },
        };

    private ServiceProvider BuildProvider(string connectionString)
    {
        _stagingRoot = Path.Combine(Path.GetTempPath(), "liakont-e2e-staging-" + CompanyId.ToString("N"));
        _archiveRoot = Path.Combine(Path.GetTempPath(), "liakont-e2e-archive-" + CompanyId.ToString("N"));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Staging:Storage:FileSystem:RootPath"] = _stagingRoot,
                ["Archive:Storage:FileSystem:RootPath"] = _archiveRoot,
            })
            .Build();

        var databaseOptions = Options.Create(new DatabaseOptions { ConnectionString = connectionString });
        _connectionFactory = new NpgsqlConnectionFactory(databaseOptions);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConnectionFactory>(_connectionFactory);
        services.AddSingleton<ISystemConnectionFactory>(_connectionFactory);
        services.AddSingleton<ITenantConnectionFactory>(new SingleDatabaseConnectionFactory(connectionString));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IOutboxWriter>(new OutboxWriter(NullLogger<OutboxWriter>.Instance));
        services.AddDataProtection();
        services.AddSingleton<ITenantContext>(new StubTenantContext(Slug));

        // Ingestion RÉELLE (réception + dédoublonnage + staging) ; Documents remplace le NoOpDocumentIntake par
        // le vrai port (DocumentIntake) — quel que soit l'ordre d'enregistrement (Replace prime sur TryAdd).
        services.AddIngestionModule();
        services.AddDocumentsModule();
        services.AddTvaMappingModule();
        services.AddValidationModule();
        services.AddTenantSettingsModule();
        services.AddStagingModule(config);
        services.AddPipelineModule();
        services.AddArchiveModule(config);

        services.AddSingleton<IPaClientRegistry>(new MutablePaClientRegistry(() => PaClient));
        services.AddScoped<IArchivedDocumentProbe>(sp =>
            new TestArchivedDocumentProbe(sp.GetRequiredService<IArchiveStore>(), Slug));

        return services.BuildServiceProvider();
    }

    private async Task ConfigurePaClientAsync()
    {
        // PA générale (toutes capacités V1), publiée, avec un tax report rattaché à l'émission (pour le SYNC).
        PaClient = new FakePaClient(new FakePaClientOptions
        {
            IssuedTaxReportIds = E2ETaxReportIds,
            TaxReports = new[]
            {
                new PaTaxReport
                {
                    Id = E2ETaxReportId,
                    Type = "reglement",
                    State = PaTaxReportState.Registered,
                    XmlBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("<TaxReport/>")),
                },
            },
        });
        await PaClient.EnsureTaxReportSettingAsync(new PaTaxReportSettingRequest
        {
            StartDate = new DateOnly(2026, 1, 1),
            TypeOperation = "LBS",
            EnterpriseSize = "PME",
        });
    }

    private async Task SeedTenantProfileAsync(string connectionString)
    {
        const string sql = """
            INSERT INTO tenantsettings.tenant_profiles
                (company_id, siren, raison_sociale, address_street, address_postal_code, address_city, address_country)
            VALUES
                (@CompanyId, @Siren, @RaisonSociale, @Street, @PostalCode, @City, @Country)
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(sql, new
        {
            CompanyId,
            Siren = ProfileSiren,
            RaisonSociale = "Étude Fictïve SVV",
            Street = "3 quai des Brumes",
            PostalCode = "35000",
            City = "Rennes",
            Country = "FR",
        });
    }

    private async Task SeedValidatedMappingTableAsync()
    {
        var rule = new MappingRule
        {
            SourceRegimeCode = "NORMAL",
            Label = "Assujetti 20 %",
            Part = MappingPart.Autre,
            Category = VatCategory.S,
            RateMode = RateMode.Fixed,
            RateValue = 20m,
        };

        var table = MappingTable.Create(
            CompanyId,
            MappingVersion,
            "Expert-comptable CMP",
            new DateOnly(2026, 7, 15),
            MappingDefaultBehavior.Block,
            new[] { rule });

        await using var scope = _provider!.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<ITvaMappingUnitOfWorkFactory>();
        await using var uow = await factory.BeginAsync();
        await uow.InsertMappingTableAsync(table);
        await uow.CommitAsync();
    }

    private async Task SeedActivePaAccountAsync(string connectionString)
    {
        const string sql = """
            INSERT INTO tenantsettings.pa_accounts
                (id, company_id, plugin_type, environment, account_identifiers, encrypted_api_key, is_active, created_at)
            VALUES
                (@Id, @CompanyId, @PluginType, @Environment, @AccountIdentifiers, NULL, true, now())
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(sql, new
        {
            Id = Guid.NewGuid(),
            CompanyId,
            PluginType = FakePaClientFactory.PaTypeKey,
            Environment = 0,
            AccountIdentifiers = "{}",
        });
    }

    private sealed class StubTenantContext : ITenantContext
    {
        public StubTenantContext(string tenantId) => TenantId = tenantId;

        public string? TenantId { get; }

        public bool IsResolved => !string.IsNullOrWhiteSpace(TenantId);
    }

    private sealed class SingleDatabaseConnectionFactory : ITenantConnectionFactory
    {
        private readonly string _connectionString;

        public SingleDatabaseConnectionFactory(string connectionString) => _connectionString = connectionString;

        public async Task<IDbConnection> OpenAsync(string? tenantId, CancellationToken cancellationToken = default)
        {
            var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
    }

    private sealed class MutablePaClientRegistry : IPaClientRegistry
    {
        private readonly Func<IPaClient> _client;

        public MutablePaClientRegistry(Func<IPaClient> client) => _client = client;

        public IReadOnlyCollection<string> RegisteredTypes => new[] { FakePaClientFactory.PaTypeKey };

        public IPaClient Resolve(PaAccountDescriptor account) => _client();

        public bool IsRegistered(string paType) => true;
    }

    private sealed class TestArchivedDocumentProbe : IArchivedDocumentProbe
    {
        private readonly IArchiveStore _store;
        private readonly string _tenant;

        public TestArchivedDocumentProbe(IArchiveStore store, string tenant)
        {
            _store = store;
            _tenant = tenant;
        }

        public Task<bool> IsArchivedAsync(ArchivedDocumentLocator locator, CancellationToken cancellationToken = default)
        {
            var directory = ArchivePackageLayout.PackageDirectory(locator.IssueYear, locator.IssueMonth, locator.DocumentNumber);
            var manifest = ArchivePackageLayout.Combine(directory, ArchivePackageLayout.ManifestFileName);
            return _store.ExistsAsync(_tenant, manifest, cancellationToken);
        }
    }

    private sealed class ProviderTenantScopeFactory : ITenantScopeFactory
    {
        private readonly IServiceProvider _root;

        public ProviderTenantScopeFactory(IServiceProvider root) => _root = root;

        public ITenantScope Create(string tenantId) => new ProviderTenantScope(tenantId, _root.CreateAsyncScope());
    }

    private sealed class ProviderTenantScope : ITenantScope
    {
        private readonly AsyncServiceScope _scope;

        public ProviderTenantScope(string tenantId, AsyncServiceScope scope)
        {
            TenantId = tenantId;
            _scope = scope;
        }

        public string TenantId { get; }

        public IServiceProvider Services => _scope.ServiceProvider;

        public ValueTask DisposeAsync() => _scope.DisposeAsync();
    }
}
