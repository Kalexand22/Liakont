namespace Liakont.Modules.Pipeline.Tests.Integration.Aggregation;

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
using Liakont.Modules.Documents.Infrastructure;
using Liakont.Modules.Ingestion.Contracts;
using Liakont.Modules.Pipeline.Application;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Domain.Payments;
using Liakont.Modules.Pipeline.Domain.Ventilation;
using Liakont.Modules.Payments.Infrastructure;
using Liakont.Modules.Pipeline.Infrastructure;
using Liakont.Modules.Pipeline.Infrastructure.Aggregation;
using Liakont.Modules.Pipeline.Infrastructure.Check;
using Liakont.Modules.Staging.Contracts;
using Liakont.Modules.Staging.Infrastructure;
using Liakont.Modules.TenantSettings.Domain.Entities;
using Liakont.Modules.TenantSettings.Infrastructure;
using Liakont.Modules.Transmission.Contracts;
using Liakont.Modules.TvaMapping.Application;
using Liakont.Modules.TvaMapping.Domain.Entities;
using Liakont.Modules.TvaMapping.Infrastructure;
using Liakont.Modules.Validation.Infrastructure;
using Liakont.PaClients.Fake;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Stratum.Common.Abstractions.Jobs;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.Database;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Harnais d'intégration de l'agrégation de paiement (PIP03a) : une base tenant PostgreSQL réelle
/// (Testcontainers) avec les migrations des modules consommés (Documents, TvaMapping, Validation,
/// TenantSettings, Staging, Pipeline, Payments) et leurs VRAIS services. Le CHECK réel écrit le snapshot de
/// ventilation (ADR-0015) ; le snapshot SURVIT à la purge du staging ; l'agrégateur réel relit le snapshot
/// et produit la projection jour×taux. Le plug-in PA factice (capacité configurable) est résolu via
/// <see cref="IPaClientRegistry"/> — jamais un plug-in concret côté pipeline. Une base par classe de test.
/// </summary>
public sealed class PaymentAggregationHarness : IAsyncLifetime
{
    /// <summary>Slug du tenant (la base unique l'ignore).</summary>
    public const string TenantSlug = "acme";

    /// <summary>SIREN émetteur FICTIF mais valide (Luhn) du profil tenant.</summary>
    public const string ProfileSiren = "404833048";

    /// <summary>Version de la table de mapping seedée (consignée à la mise à ReadyToSend).</summary>
    public const string MappingVersion = "cmp-v1";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private ServiceProvider? _provider;
    private string? _stagingRoot;

    /// <summary>Identité de l'unique société du tenant.</summary>
    public Guid CompanyId { get; } = Guid.NewGuid();

    /// <summary>Chaîne de connexion de la base tenant réelle.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <summary>Plug-in PA factice (capacité configurable par test).</summary>
    public FakePaClient PaClient { get; private set; } = new();

    /// <summary>Fabrique de scope tenant de test (au-dessus du fournisseur racine).</summary>
    public ITenantScopeFactory ScopeFactory => new ProviderTenantScopeFactory(_provider!);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        RunCommonMigrations();
        RunModuleMigrations(typeof(DocumentsModuleRegistration).Assembly);
        RunModuleMigrations(typeof(TvaMappingModuleRegistration).Assembly);
        RunModuleMigrations(typeof(TenantSettingsModuleRegistration).Assembly);
        RunModuleMigrations(typeof(StagingModuleRegistration).Assembly);
        RunModuleMigrations(typeof(PipelineModuleRegistration).Assembly);
        RunModuleMigrations(typeof(Liakont.Modules.Payments.Infrastructure.PaymentsModuleRegistration).Assembly);

        _provider = BuildProvider();

        await SeedTenantProfileAsync();
        await SeedValidatedMappingTableAsync();
        await SeedActivePaAccountAsync();
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }

        if (_stagingRoot is not null && Directory.Exists(_stagingRoot))
        {
            Directory.Delete(_stagingRoot, recursive: true);
        }

        await _container.DisposeAsync();
    }

    /// <summary>Configure la capacité de transmission des paiements (Flux 10.4) du plug-in factice.</summary>
    public void SetPaymentReportingCapability(bool supported)
    {
        PaClient = new FakePaClient(new FakePaClientOptions
        {
            Capabilities = new PaCapabilities
            {
                PaName = FakePaClientOptions.DefaultPaName,
                SupportsDomesticPaymentReporting = supported,
            },
        });
    }

    /// <summary>Ingestion (Detected) + staging + CHECK réel (écrit le snapshot ADR-0015, passe ReadyToSend). Retourne l'empreinte.</summary>
    public async Task<string> CheckServiceDocumentAsync(Guid documentId, PivotDocumentDto pivot)
    {
        var json = CanonicalJson.Serialize(pivot);
        var hash = PayloadHasher.ComputeHash(json);

        await using (var scope = _provider!.CreateAsyncScope())
        {
            var intake = scope.ServiceProvider.GetRequiredService<IDocumentIntake>();
            await intake.RegisterDetectedDocumentAsync(new DetectedDocumentIntake
            {
                DocumentId = documentId,
                TenantId = TenantSlug,
                SourceReference = pivot.SourceReference,
                PayloadHash = hash,
                Document = pivot,
                ReceivedAtUtc = DateTimeOffset.UtcNow,
            });

            var staging = scope.ServiceProvider.GetRequiredService<IPayloadStagingStore>();
            await staging.WriteAsync(new StagedPayloadKey(TenantSlug, documentId, hash), json);
        }

        var consumer = new DocumentReceivedConsumer(ScopeFactory, NullLogger<DocumentReceivedConsumer>.Instance);
        await consumer.HandleAsync(BuildEvent(documentId, pivot.SourceReference, hash));
        return hash;
    }

    /// <summary>Purge le contenu stagé d'un document (simule la purge ADR-0014 après émission).</summary>
    public async Task PurgeStagingAsync(Guid documentId, string payloadHash)
    {
        await using var scope = _provider!.CreateAsyncScope();
        var staging = scope.ServiceProvider.GetRequiredService<IPayloadStagingStore>();
        await staging.PurgeAsync(new StagedPayloadKey(TenantSlug, documentId, payloadHash));
    }

    /// <summary>Indique si le contenu pivot est encore présent dans le staging.</summary>
    public async Task<bool> IsStagedAsync(Guid documentId, string payloadHash)
    {
        await using var scope = _provider!.CreateAsyncScope();
        var staging = scope.ServiceProvider.GetRequiredService<IPayloadStagingStore>();
        return await staging.ExistsAsync(new StagedPayloadKey(TenantSlug, documentId, payloadHash));
    }

    /// <summary>Snapshot de ventilation d'un document (ADR-0015), ou <c>null</c>.</summary>
    public async Task<VentilationSnapshot?> GetSnapshotAsync(Guid documentId)
    {
        await using var scope = _provider!.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IVentilationSnapshotStore>();
        return await store.GetAsync(documentId, MappingVersion);
    }

    /// <summary>Tente d'écrire un snapshot via le store réel ; retourne <c>true</c> si inséré, <c>false</c> si déjà présent (idempotence).</summary>
    public async Task<bool> SaveSnapshotAsync(VentilationSnapshot snapshot)
    {
        await using var scope = _provider!.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IVentilationSnapshotStore>();
        return await store.SaveAsync(snapshot);
    }

    /// <summary>Exécute une commande SQL brute sur la base tenant (propage l'exception base — pour tester le rejet append-only).</summary>
    public async Task ExecuteRawAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(sql);
    }

    /// <summary>Paramétrage fiscal du tenant (insertion directe ; null = suspension).</summary>
    public async Task SetFiscalSettingsAsync(
        bool? vatOnDebits,
        Liakont.Modules.TenantSettings.Domain.Entities.OperationCategory? operationCategory,
        string? reportingFrequency,
        FeeImputationMethod? feeImputationMethod)
    {
        const string sql = """
            INSERT INTO tenantsettings.fiscal_settings
                (id, company_id, vat_on_debits, operation_category, reporting_frequency, fee_imputation_method, created_at)
            VALUES
                (@Id, @CompanyId, @VatOnDebits, @OperationCategory, @ReportingFrequency, @FeeImputationMethod, now())
            ON CONFLICT (company_id) DO UPDATE SET
                vat_on_debits = EXCLUDED.vat_on_debits,
                operation_category = EXCLUDED.operation_category,
                reporting_frequency = EXCLUDED.reporting_frequency,
                fee_imputation_method = EXCLUDED.fee_imputation_method,
                updated_at = now()
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(sql, new
        {
            Id = Guid.NewGuid(),
            CompanyId,
            VatOnDebits = vatOnDebits,
            OperationCategory = operationCategory.HasValue ? (int?)operationCategory.Value : null,
            ReportingFrequency = reportingFrequency,
            FeeImputationMethod = feeImputationMethod.HasValue ? (int?)feeImputationMethod.Value : null,
        });
    }

    /// <summary>Seede un encaissement brut (insertion directe dans le module Payments).</summary>
    public async Task SeedPaymentAsync(DateOnly paymentDate, decimal amount, string? relatedDocumentNumber)
    {
        const string sql = """
            INSERT INTO payments.payments
                (id, payment_date, amount, method, related_document_number, source_reference, received_utc)
            VALUES
                (@Id, @PaymentDate, @Amount, @Method, @RelatedDocumentNumber, @SourceReference, now())
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(sql, new
        {
            Id = Guid.NewGuid(),
            PaymentDate = paymentDate,
            Amount = amount,
            Method = "Virement",
            RelatedDocumentNumber = relatedDocumentNumber,
            SourceReference = "reglement-" + Guid.NewGuid().ToString("N"),
        });
    }

    /// <summary>Exécute l'agrégation de paiement pour le tenant (scope tenant réel).</summary>
    public async Task RunAggregateAsync()
    {
        await using var scope = _provider!.CreateAsyncScope();
        var job = new PaymentAggregatorTenantJob(PipelineRunTrigger.Scheduled);
        await job.ExecuteAsync(new TenantJobContext(TenantSlug, scope.ServiceProvider));
    }

    /// <summary>Projection des agrégats de paiement du tenant.</summary>
    public async Task<IReadOnlyList<PaymentDailyAggregate>> GetAggregatesAsync()
    {
        await using var scope = _provider!.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IPaymentAggregationStore>();
        return await store.GetAllAsync();
    }

    private static Stratum.Common.Abstractions.Events.IntegrationEvent<Liakont.Modules.Ingestion.Contracts.Events.DocumentReceivedV1> BuildEvent(
        Guid documentId, string sourceReference, string payloadHash)
    {
        var payload = new Liakont.Modules.Ingestion.Contracts.Events.DocumentReceivedV1
        {
            TenantId = TenantSlug,
            DocumentId = documentId,
            SourceReference = sourceReference,
            PayloadHash = payloadHash,
            ReceivedAtUtc = DateTimeOffset.UtcNow,
        };

        return new Stratum.Common.Abstractions.Events.IntegrationEvent<Liakont.Modules.Ingestion.Contracts.Events.DocumentReceivedV1>
        {
            EventId = Guid.NewGuid(),
            EventType = "ingestion.document.received",
            OccurredAt = DateTimeOffset.UtcNow,
            CorrelationId = Guid.NewGuid(),
            ModuleSource = "Ingestion",
            Payload = payload,
            Version = 1,
        };
    }

    private ServiceProvider BuildProvider()
    {
        _stagingRoot = Path.Combine(Path.GetTempPath(), "liakont-pip03a-" + CompanyId.ToString("N"));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Staging:Storage:FileSystem:RootPath"] = _stagingRoot,
            })
            .Build();

        var databaseOptions = Options.Create(new DatabaseOptions { ConnectionString = ConnectionString });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConnectionFactory>(new NpgsqlConnectionFactory(databaseOptions));
        services.AddSingleton<ITenantConnectionFactory>(new SingleDatabaseConnectionFactory(ConnectionString));
        services.AddSingleton(TimeProvider.System);
        services.AddDataProtection();

        services.AddDocumentsModule();
        services.AddTvaMappingModule();
        services.AddValidationModule();
        services.AddTenantSettingsModule();
        services.AddStagingModule(config);
        services.AddPipelineModule();
        services.AddPaymentsModule();

        services.AddSingleton<IPaClientRegistry>(new MutablePaClientRegistry(() => PaClient));

        return services.BuildServiceProvider();
    }

    private async Task SeedTenantProfileAsync()
    {
        const string sql = """
            INSERT INTO tenantsettings.tenant_profiles
                (company_id, siren, raison_sociale, address_street, address_postal_code, address_city, address_country)
            VALUES
                (@CompanyId, @Siren, @RaisonSociale, @Street, @PostalCode, @City, @Country)
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
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

    private async Task SeedActivePaAccountAsync()
    {
        const string sql = """
            INSERT INTO tenantsettings.pa_accounts
                (id, company_id, plugin_type, environment, account_identifiers, encrypted_api_key, is_active, created_at)
            VALUES
                (@Id, @CompanyId, @PluginType, @Environment, @AccountIdentifiers, NULL, true, now())
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
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

    private void RunCommonMigrations()
    {
        var options = Options.Create(new DatabaseOptions { ConnectionString = ConnectionString });
        var migrationOptions = Options.Create(new MigrationAssembliesOptions());
        var runner = new MigrationRunner(options, migrationOptions, NullLogger<MigrationRunner>.Instance);
        runner.MigrateUp();
    }

    private void RunModuleMigrations(Assembly moduleAssembly)
    {
        var upgrader = DeployChanges.To
            .PostgresqlDatabase(ConnectionString)
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

    private sealed class MutablePaClientRegistry : IPaClientRegistry
    {
        private readonly Func<IPaClient> _client;

        public MutablePaClientRegistry(Func<IPaClient> client) => _client = client;

        public IReadOnlyCollection<string> RegisteredTypes => new[] { FakePaClientFactory.PaTypeKey };

        public IPaClient Resolve(PaAccountDescriptor account) => _client();

        public bool IsRegistered(string paType) => true;
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
