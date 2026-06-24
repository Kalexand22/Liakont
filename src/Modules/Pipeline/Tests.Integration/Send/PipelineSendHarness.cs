namespace Liakont.Modules.Pipeline.Tests.Integration.Send;

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using DbUp;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Serialization;
using Liakont.Modules.Archive.Domain;
using Liakont.Modules.Archive.Infrastructure;
using Liakont.Modules.Documents.Contracts.Lifecycle;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Documents.Infrastructure;
using Liakont.Modules.Ingestion.Contracts;
using Liakont.Modules.Mandats.Contracts.DTOs;
using Liakont.Modules.Mandats.Contracts.Queries;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Contracts.Queries;
using Liakont.Modules.Pipeline.Infrastructure;
using Liakont.Modules.Staging.Contracts;
using Liakont.Modules.Staging.Infrastructure;
using Liakont.Modules.TenantSettings.Infrastructure;
using Liakont.Modules.Transmission.Contracts;
using Liakont.Modules.TvaMapping.Application;
using Liakont.Modules.TvaMapping.Domain.Entities;
using Liakont.Modules.TvaMapping.Infrastructure;
using Liakont.PaClients.Fake;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.Database;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Harnais d'intégration du SEND (PIP01c) : une base tenant PostgreSQL réelle (Testcontainers) avec les
/// migrations des modules consommés (Documents, TenantSettings, Staging, Pipeline, Archive), leurs VRAIS
/// services (cycle de vie, staging, purge subordonnée au WORM, archive WORM réelle), un plug-in PA factice
/// résolu via <see cref="IPaClientRegistry"/> (jamais un plug-in concret côté pipeline) et une sonde de
/// présence WORM de test au-dessus de <see cref="IArchiveStore"/> (réplique du port câblé au Host). Une
/// base par classe de test ; chaque test seede ses propres documents.
/// </summary>
public sealed class PipelineSendHarness : IAsyncLifetime
{
    /// <summary>Slug du tenant (la base unique l'ignore).</summary>
    public const string TenantSlug = "acme";

    /// <summary>SIREN émetteur FICTIF mais valide (Luhn) du profil tenant.</summary>
    public const string ProfileSiren = "404833048";

    /// <summary>Version de table de mapping consignée à la mise à ReadyToSend.</summary>
    public const string MappingVersion = "cmp-v1";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private readonly ProbeState _probeState = new();
    private readonly FakeSelfBilledAcceptanceQueries _acceptanceQueries = new();

    private ServiceProvider? _provider;
    private string? _stagingRoot;
    private string? _archiveRoot;

    /// <summary>Identité de l'unique société du tenant.</summary>
    public Guid CompanyId { get; } = Guid.NewGuid();

    /// <summary>Chaîne de connexion de la base tenant réelle.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <summary>Plug-in PA factice partagé (configuré et inspecté par chaque test).</summary>
    public FakePaClient PaClient { get; private set; } = new();

    /// <summary>Force la sonde WORM à répondre « absent » (simule l'écart entre transition Issued et écriture WORM).</summary>
    public bool ForceWormAbsent
    {
        get => _probeState.ForceAbsent;
        set => _probeState.ForceAbsent = value;
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        RunCommonMigrations();
        RunModuleMigrations(typeof(DocumentsModuleRegistration).Assembly);
        RunModuleMigrations(typeof(TvaMappingModuleRegistration).Assembly);
        RunModuleMigrations(typeof(TenantSettingsModuleRegistration).Assembly);
        RunModuleMigrations(typeof(StagingModuleRegistration).Assembly);
        RunModuleMigrations(typeof(PipelineModuleRegistration).Assembly);
        RunModuleMigrations(typeof(ArchiveModuleRegistration).Assembly);

        _provider = BuildProvider();

        await SeedTenantProfileAsync();
        await SeedActivePaAccountAsync();
        await SeedValidatedMappingTableAsync();
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

    /// <summary>
    /// Nombre d'appels d'une méthode du plug-in factice CIBLANT un détail donné (numéro de document ou
    /// référence PA). Le SEND est un job TENANT-WIDE : les assertions doivent être SPÉCIFIQUES au document du
    /// test (la piste d'audit est append-only — on ne purge pas la base entre tests).
    /// </summary>
    public int PaCallCount(string method, string detail) =>
        PaClient.Calls.Count(call => call.Method == method && call.Detail == detail);

    /// <summary>Remplace le plug-in factice par un plug-in NON publié (SIREN non publié) pour un test.</summary>
    public void UseUnpublishedFake() => PaClient = new FakePaClient();

    /// <summary>Configure (et remplace) le plug-in PA factice pour un test, et le rend « publié » (SIREN publié).</summary>
    public async Task UsePublishedFakeAsync(FakePaClientOptions? options = null)
    {
        PaClient = new FakePaClient(options);
        await PaClient.EnsureTaxReportSettingAsync(new PaTaxReportSettingRequest
        {
            StartDate = new DateOnly(2026, 1, 1),
            TypeOperation = "LBS",
            EnterpriseSize = "PME",
        });
    }

    /// <summary>Seede un document <c>Detected</c> (chemin d'ingestion) + son contenu pivot stagé (PIP00).</summary>
    public async Task<string> SeedDetectedAndStageAsync(Guid documentId, PivotDocumentDto pivot)
    {
        var json = CanonicalJson.Serialize(pivot);
        var hash = PayloadHasher.ComputeHash(json);

        await using var scope = _provider!.CreateAsyncScope();
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
        return hash;
    }

    /// <summary>Fait passer un document <c>Detected</c> → <c>ReadyToSend</c> (version de mapping consignée).</summary>
    public Task MarkReadyToSendAsync(Guid documentId) =>
        WithLifecycle(lifecycle => lifecycle.MarkReadyToSendAsync(documentId, MappingVersion));

    /// <summary>
    /// Seede l'acceptation self-billed lue par le SEND (MND07) : état + BT-1 fiscal alloué (MND05). Représente
    /// une précondition (acceptation acquise + allocation faite), comme les tests MND02-05 seedent la leur.
    /// </summary>
    public void SeedSelfBilledAcceptance(Guid documentId, string? allocatedNumber, bool isAccepted = true, string state = "Accepted") =>
        _acceptanceQueries.Set(new SelfBilledAcceptanceDto
        {
            DocumentId = documentId,
            State = state,
            AllocatedNumber = allocatedNumber,
            PendingSince = DateTimeOffset.UtcNow,
            DeadlineUtc = null,
            IsAccepted = isAccepted,
        });

    /// <summary>Engage la transmission (<c>ReadyToSend</c> → <c>Sending</c>).</summary>
    public Task BeginSendingAsync(Guid documentId) =>
        WithLifecycle(lifecycle => lifecycle.BeginSendingAsync(documentId));

    /// <summary>Marque une erreur technique (<c>Sending</c> → <c>TechnicalError</c>).</summary>
    public Task MarkTechnicalErrorAsync(Guid documentId) =>
        WithLifecycle(lifecycle => lifecycle.MarkTechnicalErrorAsync(documentId));

    /// <summary>Renseigne la référence PA d'un document (simule une référence connue avant un crash).</summary>
    public async Task SetPaDocumentIdAsync(Guid documentId, string paDocumentId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "UPDATE documents.documents SET pa_document_id = @PaDocumentId WHERE id = @Id",
            new { PaDocumentId = paDocumentId, Id = documentId });
    }

    /// <summary>
    /// Enregistre la référence PA d'un dépôt ASYNCHRONE accepté via le PORT RÉEL (item PIPE01) : le document
    /// reste <c>Sending</c>, la référence est persistée et un fait d'audit append-only est inscrit. Exerce le
    /// round-trip réel (contraste avec <see cref="SetPaDocumentIdAsync"/> qui contourne la machine à états).
    /// </summary>
    public Task RecordPaSendingReferenceAsync(Guid documentId, string paDocumentId, string? paResponseSnapshot = null) =>
        WithLifecycle(lifecycle => lifecycle.RecordPaSendingReferenceAsync(documentId, paDocumentId, paResponseSnapshot));

    /// <summary>Référence PA réellement persistée sur le document (ou <c>null</c>).</summary>
    public async Task<string?> GetPaDocumentIdAsync(Guid documentId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<string?>(
            "SELECT pa_document_id FROM documents.documents WHERE id = @Id",
            new { Id = documentId });
    }

    /// <summary>Nombre d'événements d'audit append-only d'un type donné pour un document.</summary>
    public async Task<int> EventCountAsync(Guid documentId, string eventType)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM documents.document_events WHERE document_id = @Id AND event_type = @EventType",
            new { Id = documentId, EventType = eventType });
    }

    /// <summary>Exécute le job SEND pour le tenant (scope tenant réel au-dessus du fournisseur câblé).</summary>
    public async Task RunSendAsync(bool dryRun = false)
    {
        await using var scope = _provider!.CreateAsyncScope();
        var job = new Liakont.Modules.Pipeline.Infrastructure.Send.SendTenantJob(PipelineRunTrigger.Scheduled, dryRun);
        await job.ExecuteAsync(new Stratum.Common.Abstractions.Jobs.TenantJobContext(TenantSlug, scope.ServiceProvider));
    }

    /// <summary>Exécute le job SYNC pour le tenant (réconciliation : facture PA + tax reports → addenda WORM).</summary>
    public async Task RunSyncAsync()
    {
        await using var scope = _provider!.CreateAsyncScope();
        var job = new Liakont.Modules.Pipeline.Infrastructure.Sync.SyncTenantJob(PipelineRunTrigger.Scheduled);
        await job.ExecuteAsync(new Stratum.Common.Abstractions.Jobs.TenantJobContext(TenantSlug, scope.ServiceProvider));
    }

    /// <summary>Exécute le job e-reporting B2C de la marge (B4) pour le tenant (agrégation + transmission).</summary>
    public async Task RunB2cMarginAsync()
    {
        await using var scope = _provider!.CreateAsyncScope();
        var job = new Liakont.Modules.Pipeline.Infrastructure.B2cReporting.B2cMarginAggregatorTenantJob(PipelineRunTrigger.Scheduled);
        await job.ExecuteAsync(new Stratum.Common.Abstractions.Jobs.TenantJobContext(TenantSlug, scope.ServiceProvider));
    }

    /// <summary>
    /// Entrées du journal d'émission B2C marge (<c>pipeline.b2c_margin_emissions</c>) pour un document, dans
    /// l'ordre d'insertion (seq). Prouve l'attempt-once (Pending écrit avant le POST) et l'issue (Issued + id PA).
    /// </summary>
    public async Task<IReadOnlyList<(string Status, string? PaEmissionId)>> GetB2cMarginEmissionsAsync(Guid documentId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        var rows = await connection.QueryAsync(
            "SELECT status, pa_emission_id FROM pipeline.b2c_margin_emissions WHERE document_id = @Id ORDER BY seq",
            new { Id = documentId });
        return rows.Select(r => ((string)r.status, (string?)r.pa_emission_id)).ToList();
    }

    /// <summary>Nombre d'entrées de coffre (paquet initial + addenda) scellées pour un document.</summary>
    public async Task<int> ArchiveEntryCountAsync(Guid documentId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM documents.archive_entries WHERE document_id = @Id",
            new { Id = documentId });
    }

    /// <summary>État courant d'un document (ou <c>null</c>).</summary>
    public async Task<string?> GetDocumentStateAsync(Guid documentId)
    {
        await using var scope = _provider!.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredService<IDocumentQueries>();
        var document = await queries.GetByIdAsync(documentId);
        return document?.State;
    }

    /// <summary>
    /// Liens reporting↔pièces (B2C03) gelés pour une transmission donnée, dans le sens transmission → pièces
    /// (tenant courant). Permet de prouver que la voie d'envoi gèle bien le lien d'une déclaration 10.3 émise (B2C04).
    /// </summary>
    public async Task<IReadOnlyList<Liakont.Modules.Archive.Contracts.ReportingPieceLink>> GetReportingPieceLinksAsync(Guid documentId)
    {
        await using var scope = _provider!.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<Liakont.Modules.Archive.Contracts.IReportingPieceLinkStore>();
        return await store.GetByDocumentAsync(CompanyId, documentId);
    }

    /// <summary>Indique si le contenu pivot est encore présent dans le staging.</summary>
    public async Task<bool> IsStagedAsync(Guid documentId, string payloadHash)
    {
        await using var scope = _provider!.CreateAsyncScope();
        var staging = scope.ServiceProvider.GetRequiredService<IPayloadStagingStore>();
        return await staging.ExistsAsync(new StagedPayloadKey(TenantSlug, documentId, payloadHash));
    }

    /// <summary>Journal d'exécutions du pipeline (SEND).</summary>
    public async Task<IReadOnlyList<PipelineRunLogDto>> GetRunsAsync()
    {
        await using var scope = _provider!.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredService<IPipelineRunQueries>();
        return await queries.GetRecentRunsAsync(200);
    }

    private static void DeleteDirectory(string? path)
    {
        if (path is null || !Directory.Exists(path))
        {
            return;
        }

        // Le coffre WORM écrit des fichiers en LECTURE SEULE : on lève l'attribut avant suppression, puis on
        // avale toute erreur résiduelle — il s'agit d'un nettoyage best-effort de répertoires temporaires.
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

    private async Task WithLifecycle(Func<IDocumentLifecycle, Task> action)
    {
        await using var scope = _provider!.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<IDocumentLifecycle>());
    }

    private ServiceProvider BuildProvider()
    {
        _stagingRoot = Path.Combine(Path.GetTempPath(), "liakont-pip01c-staging-" + CompanyId.ToString("N"));
        _archiveRoot = Path.Combine(Path.GetTempPath(), "liakont-pip01c-archive-" + CompanyId.ToString("N"));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Staging:Storage:FileSystem:RootPath"] = _stagingRoot,
                ["Archive:Storage:FileSystem:RootPath"] = _archiveRoot,
            })
            .Build();

        var databaseOptions = Options.Create(new DatabaseOptions { ConnectionString = ConnectionString });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConnectionFactory>(new NpgsqlConnectionFactory(databaseOptions));
        services.AddSingleton<ITenantConnectionFactory>(new SingleDatabaseConnectionFactory(ConnectionString));
        services.AddSingleton(TimeProvider.System);
        services.AddDataProtection();

        // Contexte tenant figé (consommé par le module Archive) — la base unique EST le tenant.
        services.AddSingleton<ITenantContext>(new StubTenantContext(TenantSlug));

        services.AddDocumentsModule();
        services.AddTvaMappingModule();
        services.AddTenantSettingsModule();
        services.AddStagingModule(config);
        services.AddPipelineModule();
        services.AddArchiveModule(config);

        // Plug-in PA : un registre qui rend le plug-in factice PARTAGÉ du harnais (inspectable par les tests).
        services.AddSingleton<IPaClientRegistry>(new MutablePaClientRegistry(() => PaClient));

        // Acceptation self-billed (MND02/03/05) côté lecture : le SEND lit le BT-1 fiscal alloué pour projeter
        // le 389 (MND07). On câble une lecture factice configurable par test (les MND02-05 couvrent la vraie
        // persistance/allocation) ; un document NON self-billed ne consulte jamais cette lecture.
        services.AddSingleton<ISelfBilledAcceptanceQueries>(_acceptanceQueries);

        // Sonde de présence WORM (port du Host) réimplémentée au-dessus de IArchiveStore pour les tests.
        services.AddScoped<IArchivedDocumentProbe>(sp =>
            new TestArchivedDocumentProbe(sp.GetRequiredService<IArchiveStore>(), TenantSlug, _probeState));

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

    /// <summary>
    /// Seede la table de mapping TVA VALIDÉE du tenant (régime « NORMAL » → catégorie S 20 %), comme le harnais
    /// CHECK : depuis emitter-filled-by-platform (ADR-0031 amendé), le SEND repose la catégorie TVA au read-time
    /// (symétrique au CHECK) — sans cette table, tout document partirait en HOLD <c>TvaUnresolved</c>.
    /// </summary>
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

        // Règle PART FRAIS (B4) : le job e-reporting B2C de la marge mappe les honoraires acheteur/vendeur avec
        // Part.Frais (F03 §2.4). Le CHECK générique ne consulte QUE Part.Autre (ConsultedMappingParts) — cette
        // règle additive n'affecte donc PAS le mapping des lignes des tests SEND. Clé (code, part) distincte → INV-TVAMAPPING-003 respectée.
        var feeRule = new MappingRule
        {
            SourceRegimeCode = "NORMAL",
            Label = "Frais 20 %",
            Part = MappingPart.Frais,
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
            new[] { rule, feeRule });

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
        var runner = new MigrationRunner(options, migrationOptions, Microsoft.Extensions.Logging.Abstractions.NullLogger<MigrationRunner>.Instance);
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

    /// <summary>État mutable partagé de la sonde WORM (permet à un test de forcer « absent »).</summary>
    private sealed class ProbeState
    {
        public bool ForceAbsent { get; set; }
    }

    /// <summary>Sonde WORM de test : interroge réellement <see cref="IArchiveStore.ExistsAsync"/> (sauf si forcée absente).</summary>
    private sealed class TestArchivedDocumentProbe : IArchivedDocumentProbe
    {
        private readonly IArchiveStore _store;
        private readonly string _tenant;
        private readonly ProbeState _state;

        public TestArchivedDocumentProbe(IArchiveStore store, string tenant, ProbeState state)
        {
            _store = store;
            _tenant = tenant;
            _state = state;
        }

        public Task<bool> IsArchivedAsync(ArchivedDocumentLocator locator, CancellationToken cancellationToken = default)
        {
            if (_state.ForceAbsent)
            {
                return Task.FromResult(false);
            }

            var directory = ArchivePackageLayout.PackageDirectory(locator.IssueYear, locator.IssueMonth, locator.DocumentNumber);
            var manifest = ArchivePackageLayout.Combine(directory, ArchivePackageLayout.ManifestFileName);
            return _store.ExistsAsync(_tenant, manifest, cancellationToken);
        }
    }

    /// <summary>Registre PA qui rend le plug-in factice partagé du harnais (résolution par clé conservée côté Contracts).</summary>
    private sealed class MutablePaClientRegistry : IPaClientRegistry
    {
        private readonly Func<IPaClient> _client;

        public MutablePaClientRegistry(Func<IPaClient> client) => _client = client;

        public IReadOnlyCollection<string> RegisteredTypes => new[] { FakePaClientFactory.PaTypeKey };

        public IPaClient Resolve(PaAccountDescriptor account) => _client();

        public bool IsRegistered(string paType) => true;
    }

    /// <summary>
    /// Lecture factice de l'acceptation self-billed (MND07) : le SEND y lit le BT-1 fiscal alloué. Configurable
    /// par document (le SEND est tenant-wide) ; un document sans acceptation seedée renvoie <c>null</c>.
    /// </summary>
    private sealed class FakeSelfBilledAcceptanceQueries : ISelfBilledAcceptanceQueries
    {
        private readonly Dictionary<Guid, SelfBilledAcceptanceDto> _byDocument = [];

        public void Set(SelfBilledAcceptanceDto acceptance) => _byDocument[acceptance.DocumentId] = acceptance;

        public Task<SelfBilledAcceptanceDto?> GetAcceptance(Guid companyId, Guid documentId, CancellationToken ct = default) =>
            Task.FromResult(_byDocument.TryGetValue(documentId, out var acceptance) ? acceptance : null);

        public Task<IReadOnlyList<SelfBilledAcceptanceLogEntryDto>> GetAcceptanceLog(Guid companyId, Guid documentId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SelfBilledAcceptanceLogEntryDto>>([]);
    }

    /// <summary>Contexte tenant figé (le module Archive est tenant-scopé).</summary>
    private sealed class StubTenantContext : ITenantContext
    {
        public StubTenantContext(string tenantId) => TenantId = tenantId;

        public string? TenantId { get; }

        public bool IsResolved => !string.IsNullOrWhiteSpace(TenantId);
    }

    /// <summary>Fabrique de connexion tenant (ingestion par slug) vers l'unique base de test : le slug est ignoré.</summary>
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
