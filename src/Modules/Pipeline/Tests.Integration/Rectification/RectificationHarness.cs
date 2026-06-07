namespace Liakont.Modules.Pipeline.Tests.Integration.Rectification;

using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using DbUp;
using Liakont.Modules.Pipeline.Application;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Domain.Payments;
using Liakont.Modules.Pipeline.Infrastructure;
using Liakont.Modules.Pipeline.Infrastructure.Aggregation;
using Liakont.Modules.Pipeline.Infrastructure.Rectification;
using Liakont.Modules.TenantSettings.Infrastructure;
using Liakont.Modules.Transmission.Contracts;
using Liakont.PaClients.Fake;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Stratum.Common.Abstractions.Jobs;
using Stratum.Common.Infrastructure.Database;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Harnais d'intégration de la rectification d'e-reporting (PIP04, flux RE) : une base tenant PostgreSQL réelle
/// (Testcontainers) avec les migrations Pipeline + TenantSettings et les VRAIS services (projection
/// d'agrégation, journal append-only des rectificatifs, service de rectification, job tenant). Le plug-in PA
/// factice (capacités configurables) est résolu via <see cref="IPaClientRegistry"/> — jamais un plug-in concret
/// côté pipeline. Une base par classe de test ; chaque test utilise une PÉRIODE distincte (le journal est
/// append-only et partagé) et fixe explicitement les capacités.
/// </summary>
public sealed class RectificationHarness : IAsyncLifetime
{
    /// <summary>Slug du tenant (la base unique l'ignore).</summary>
    public const string TenantSlug = "acme";

    /// <summary>SIREN émetteur FICTIF mais valide (Luhn) du profil tenant.</summary>
    public const string ProfileSiren = "404833048";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private ServiceProvider? _provider;

    /// <summary>Identité de l'unique société du tenant.</summary>
    public Guid CompanyId { get; } = Guid.NewGuid();

    /// <summary>Chaîne de connexion de la base tenant réelle.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <summary>Plug-in PA factice (capacités configurables par test).</summary>
    public FakePaClient PaClient { get; private set; } = new();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        RunCommonMigrations();
        RunModuleMigrations(typeof(TenantSettingsModuleRegistration).Assembly);
        RunModuleMigrations(typeof(PipelineModuleRegistration).Assembly);

        _provider = BuildProvider();

        await SeedTenantProfileAsync();
        await SeedActivePaAccountAsync();
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }

        await _container.DisposeAsync();
    }

    /// <summary>
    /// Configure les capacités du plug-in factice (rectification flux RE + e-reporting de paiement domestique
    /// 10.4) et le scénario d'envoi (succès par défaut — rejet / erreur technique pour exercer les branches de
    /// <c>MapResult</c>). Réinitialise le journal d'appels.
    /// </summary>
    public void SetCapabilities(
        bool supportsRectification,
        bool supportsDomesticPaymentReporting,
        FakePaScenario sendScenario = FakePaScenario.Success)
    {
        PaClient = new FakePaClient(new FakePaClientOptions
        {
            Capabilities = new PaCapabilities
            {
                PaName = FakePaClientOptions.DefaultPaName,
                SupportsReportRectification = supportsRectification,
                SupportsDomesticPaymentReporting = supportsDomesticPaymentReporting,
            },
            SendScenario = sendScenario,
        });
    }

    /// <summary>Remplace la projection d'agrégation jour×taux du tenant (état courant corrigé) par le jeu fourni.</summary>
    public async Task SeedAggregatesAsync(IReadOnlyList<PaymentDailyAggregate> aggregates)
    {
        await using var scope = _provider!.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IPaymentAggregationStore>();
        await store.ReplaceAllAsync(aggregates);
    }

    /// <summary>Rectifie une période (scope tenant réel) et retourne l'issue.</summary>
    public async Task<ReportRectificationOutcome> RectifyAsync(PaymentReportFlux flux, DateOnly periodStart, DateOnly periodEnd)
    {
        await using var scope = _provider!.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ReportRectificationService>();
        return await service.RectifyPeriodAsync(TenantSlug, flux, periodStart, periodEnd);
    }

    /// <summary>Exécute le job de rectification du tenant (ré-évaluation des périodes déjà déclarées).</summary>
    public async Task RunRectifyJobAsync()
    {
        await using var scope = _provider!.CreateAsyncScope();
        var job = new ReportRectificationTenantJob(PipelineRunTrigger.Scheduled);
        await job.ExecuteAsync(new TenantJobContext(TenantSlug, scope.ServiceProvider));
    }

    /// <summary>Historique chronologique complet d'une période (initiale + rectificatifs).</summary>
    public async Task<IReadOnlyList<ReportRectificationEntry>> GetHistoryAsync(PaymentReportFlux flux, DateOnly periodStart, DateOnly periodEnd)
    {
        await using var scope = _provider!.CreateAsyncScope();
        var ledger = scope.ServiceProvider.GetRequiredService<IReportRectificationLedger>();
        return await ledger.ListByPeriodAsync(flux, periodStart, periodEnd);
    }

    /// <summary>Nombre d'exécutions de pipeline d'un type donné (lecture du journal des runs).</summary>
    public async Task<int> CountRunLogsAsync(PipelineRunType runType)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM pipeline.run_logs WHERE run_type = @RunType",
            new { RunType = runType.ToString() });
    }

    /// <summary>Exécute une commande SQL brute sur la base tenant (propage l'exception base — pour tester le rejet append-only).</summary>
    public async Task ExecuteRawAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(sql);
    }

    private ServiceProvider BuildProvider()
    {
        var databaseOptions = Options.Create(new DatabaseOptions { ConnectionString = ConnectionString });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConnectionFactory>(new NpgsqlConnectionFactory(databaseOptions));
        services.AddSingleton<ITenantConnectionFactory>(new SingleDatabaseConnectionFactory(ConnectionString));
        services.AddSingleton(TimeProvider.System);
        services.AddDataProtection();

        services.AddTenantSettingsModule();
        services.AddPipelineModule();

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
}
