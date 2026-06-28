namespace Liakont.Modules.Pipeline.Tests.Integration.Check;

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
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Documents.Infrastructure;
using Liakont.Modules.Ingestion.Contracts;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Contracts.Queries;
using Liakont.Modules.Pipeline.Infrastructure;
using Liakont.Modules.Staging.Contracts;
using Liakont.Modules.Staging.Infrastructure;
using Liakont.Modules.TenantSettings.Infrastructure;
using Liakont.Modules.TvaMapping.Application;
using Liakont.Modules.TvaMapping.Domain.Entities;
using Liakont.Modules.TvaMapping.Infrastructure;
using Liakont.Modules.Validation.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.Database;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Harnais d'intégration du CHECK : une base tenant PostgreSQL réelle (Testcontainers) sur laquelle on
/// applique les migrations des modules consommés (Documents, TvaMapping, TenantSettings, Pipeline) et on
/// câble leurs VRAIS services (mapping, validation, cycle de vie, staging, paramétrage). Le consommateur
/// CHECK est invité à résoudre un scope tenant via un <see cref="ITenantScopeFactory"/> de test (le seam
/// du Host) au-dessus du fournisseur câblé. Une seule base par classe de test (database-per-tenant
/// simulée par une base unique : le slug est ignoré) ; l'état partagé (profil tenant + table validée) est
/// seedé une fois, chaque test seedant son propre document pour ne pas polluer les autres.
/// </summary>
public sealed class PipelineCheckHarness : IAsyncLifetime
{
    /// <summary>Slug du tenant (porté par l'événement ; la base unique l'ignore).</summary>
    public const string TenantSlug = "acme";

    /// <summary>SIREN émetteur FICTIF mais valide (Luhn) du profil tenant — exigé par SupplierIdentityRule.</summary>
    public const string ProfileSiren = "404833048";

    /// <summary>Version de la table de mapping seedée.</summary>
    public const string MappingVersion = "cmp-v1";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private ServiceProvider? _provider;
    private string? _stagingRoot;

    /// <summary>Identité de l'unique société du tenant (seedée dans tenant_profiles).</summary>
    public Guid CompanyId { get; } = Guid.NewGuid();

    /// <summary>Chaîne de connexion de la base tenant réelle.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <summary>Fournisseur racine câblant les vrais services des modules.</summary>
    public IServiceProvider Provider => _provider!;

    /// <summary>Fabrique de scope tenant de test (au-dessus du fournisseur racine).</summary>
    public ITenantScopeFactory ScopeFactory => new ProviderTenantScopeFactory(_provider!);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        RunCommonMigrations();
        RunModuleMigrations(typeof(DocumentsModuleRegistration).Assembly);
        RunModuleMigrations(typeof(TvaMappingModuleRegistration).Assembly);
        RunModuleMigrations(typeof(TenantSettingsModuleRegistration).Assembly);
        RunModuleMigrations(typeof(PipelineModuleRegistration).Assembly);

        _provider = BuildProvider();

        await SeedTenantProfileAsync();
        await SeedValidatedMappingTableAsync();
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

    /// <summary>Crée un document <c>Detected</c> via l'ingestion (chemin de production).</summary>
    public async Task SeedDetectedDocumentAsync(Guid documentId, string sourceReference, string payloadHash, PivotDocumentDto pivot)
    {
        await using var scope = _provider!.CreateAsyncScope();
        var intake = scope.ServiceProvider.GetRequiredService<IDocumentIntake>();
        await intake.RegisterDetectedDocumentAsync(new DetectedDocumentIntake
        {
            DocumentId = documentId,
            TenantId = TenantSlug,
            SourceReference = sourceReference,
            PayloadHash = payloadHash,
            Document = pivot,
            ReceivedAtUtc = DateTimeOffset.UtcNow,
        });
    }

    /// <summary>Écrit le contenu pivot canonique dans le magasin de staging (PIP00).</summary>
    public async Task StagePayloadAsync(Guid documentId, string payloadHash, string canonicalJson)
    {
        await using var scope = _provider!.CreateAsyncScope();
        var staging = scope.ServiceProvider.GetRequiredService<IPayloadStagingStore>();
        await staging.WriteAsync(new StagedPayloadKey(TenantSlug, documentId, payloadHash), canonicalJson);
    }

    /// <summary>
    /// Écrit (UPSERT) les mentions de facturation du tenant (BUG-26, F12-A §3.4) — termes de paiement (BT-20) +
    /// mentions légales FR (PMD/PMT/AAB). Contenus FICTIFS (CLAUDE.md n°7). Une ligne par société (clé unique).
    /// </summary>
    public async Task SeedBillingMentionsAsync(
        string paymentTerms = "Paiement à 30 jours fin de mois.",
        string latePenaltyTerms = "Pénalités de retard au taux légal.",
        string recoveryFeeTerms = "Indemnité forfaitaire de recouvrement de 40 €.",
        string discountTerms = "Pas d'escompte pour paiement anticipé.")
    {
        const string sql = """
            INSERT INTO tenantsettings.billing_mentions
                (id, company_id, payment_terms, late_penalty_terms, recovery_fee_terms, discount_terms, created_at)
            VALUES
                (@Id, @CompanyId, @PaymentTerms, @LatePenaltyTerms, @RecoveryFeeTerms, @DiscountTerms, now())
            ON CONFLICT (company_id) DO UPDATE SET
                payment_terms = EXCLUDED.payment_terms,
                late_penalty_terms = EXCLUDED.late_penalty_terms,
                recovery_fee_terms = EXCLUDED.recovery_fee_terms,
                discount_terms = EXCLUDED.discount_terms,
                updated_at = now()
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(sql, new
        {
            Id = Guid.NewGuid(),
            CompanyId,
            PaymentTerms = paymentTerms,
            LatePenaltyTerms = latePenaltyTerms,
            RecoveryFeeTerms = recoveryFeeTerms,
            DiscountTerms = discountTerms,
        });
    }

    /// <summary>Supprime les mentions de facturation du tenant (pour exercer le blocage CHECK quand elles manquent).</summary>
    public async Task RemoveBillingMentionsAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "DELETE FROM tenantsettings.billing_mentions WHERE company_id = @CompanyId", new { CompanyId });
    }

    /// <summary>État courant d'un document (ou <c>null</c> s'il n'existe pas).</summary>
    public async Task<string?> GetDocumentStateAsync(Guid documentId)
    {
        await using var scope = _provider!.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredService<IDocumentQueries>();
        var document = await queries.GetByIdAsync(documentId);
        return document?.State;
    }

    /// <summary>Piste d'audit d'un document (pour vérifier la persistance des motifs de blocage).</summary>
    public async Task<IReadOnlyList<DocumentEventDto>> GetEventsAsync(Guid documentId)
    {
        await using var scope = _provider!.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredService<IDocumentQueries>();
        return await queries.GetEventsAsync(documentId);
    }

    /// <summary>Journal d'exécutions du pipeline (CHECK).</summary>
    public async Task<IReadOnlyList<PipelineRunLogDto>> GetRunsAsync()
    {
        await using var scope = _provider!.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredService<IPipelineRunQueries>();
        return await queries.GetRecentRunsAsync(200);
    }

    /// <summary>
    /// Rejeu read-time du contenu d'un document (BUG-5, <see cref="IDocumentContentReplayService"/>) dans un scope
    /// résolu sur le tenant de test — le chemin EXACT consommé par la console pour afficher les lignes avant
    /// transmission.
    /// </summary>
    public async Task<DocumentContentReplay> ReplayContentAsync(Guid documentId)
    {
        await using var scope = _provider!.CreateAsyncScope();
        var replay = scope.ServiceProvider.GetRequiredService<IDocumentContentReplayService>();
        return await replay.ReplayAsync(documentId);
    }

    private ServiceProvider BuildProvider()
    {
        _stagingRoot = Path.Combine(Path.GetTempPath(), "liakont-pip01b-" + CompanyId.ToString("N"));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Staging:Storage:FileSystem:RootPath"] = _stagingRoot,
            })
            .Build();

        // NpgsqlConnectionFactory enregistre (dans son ctor) le DateOnlyTypeHandler Dapper GLOBAL : sans lui,
        // l'écriture d'un DateOnly (date de validation de la table TVA, date d'émission…) lève. On l'utilise
        // pour IConnectionFactory ; ITenantConnectionFactory (ingestion par slug) garde la base unique.
        var databaseOptions = Options.Create(new DatabaseOptions { ConnectionString = ConnectionString });

        var services = new ServiceCollection();
        services.AddSingleton<IConnectionFactory>(new NpgsqlConnectionFactory(databaseOptions));
        services.AddSingleton<ITenantConnectionFactory>(new SingleDatabaseConnectionFactory(ConnectionString));
        services.AddSingleton(TimeProvider.System);

        // Contexte tenant résolu sur le slug de la base de test (BUG-5 : le rejeu read-time lit le pivot stagé
        // via une clé tenant-scopée). La base unique EST le tenant — le slug fixe suffit.
        services.AddSingleton<ITenantContext>(new FixedTenantContext(TenantSlug));

        services.AddDataProtection();

        services.AddDocumentsModule();
        services.AddTvaMappingModule();
        services.AddValidationModule();
        services.AddTenantSettingsModule();
        services.AddStagingModule(config);
        services.AddPipelineModule();

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

        // Régime de la marge (Part.Autre) → E + VATEX-EU-J : signal validé permettant à la plateforme de DÉRIVER
        // le marqueur de déclaration de marge B2C au CHECK (B2cMarginMarking) et d'exercer la garde fail-closed
        // « marge non classée » (honoraires + exonéré + acheteur pro → bloqué). Clé (MARGE, Autre) distincte.
        var marginAdjudicationRule = new MappingRule
        {
            SourceRegimeCode = "MARGE",
            Label = "Régime de la marge — objets de collection (art. 297 A)",
            Part = MappingPart.Autre,
            Category = VatCategory.E,
            Vatex = "VATEX-EU-J",
            RateMode = RateMode.Fixed,
            RateValue = 0m,
        };

        var table = MappingTable.Create(
            CompanyId,
            MappingVersion,
            "Expert-comptable CMP",
            new DateOnly(2026, 7, 15),
            MappingDefaultBehavior.Block,
            new[] { rule, marginAdjudicationRule });

        await using var scope = _provider!.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<ITvaMappingUnitOfWorkFactory>();
        await using var uow = await factory.BeginAsync();
        await uow.InsertMappingTableAsync(table);
        await uow.CommitAsync();
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

    /// <summary>Fabrique de connexion tenant (ingestion par slug) vers l'unique base de test : le slug est
    /// ignoré (la base de test EST le tenant). Les handlers de type Dapper sont enregistrés globalement par
    /// <see cref="NpgsqlConnectionFactory"/> (IConnectionFactory) — ils valent pour ces connexions aussi.</summary>
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

    /// <summary>Contexte tenant fixe (la base de test EST le tenant — le slug est constant).</summary>
    private sealed class FixedTenantContext : ITenantContext
    {
        public FixedTenantContext(string tenantId) => TenantId = tenantId;

        public string? TenantId { get; }

        public bool IsResolved => !string.IsNullOrWhiteSpace(TenantId);
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
