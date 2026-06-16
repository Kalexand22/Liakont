namespace Liakont.Modules.Mandats.Tests.Integration;

using System.Reflection;
using Dapper;
using DbUp;
using FluentAssertions;
using Liakont.Modules.DocumentApproval.Infrastructure;
using Liakont.Modules.Mandats.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stratum.Common.Infrastructure.Database;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Test de migration de bascule V010 avec données réelles (SIG05, ADR-0024 amendé par ADR-0028).
/// Sème l'état pré-V010 (<c>mandats.self_billed_acceptances</c> + <c>mandats.self_billed_acceptance_log</c>),
/// applique V010 seule, puis vérifie la relocalisation sans perte vers <c>documentapproval.document_validations</c>
/// + <c>documentapproval.document_approval_log</c> et la suppression du journal source. Utilise son propre
/// conteneur PostgreSQL : le conteneur partagé "MandatsIntegration" a déjà appliqué V010 sur base vide — ce
/// test doit vérifier la migration sur des données réelles, indépendamment.
/// </summary>
public sealed class SelfBilledAcceptanceMigrationV010Tests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        RunCommonMigrations();
        RunModuleMigrationsExceptV010();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task V010_Relocates_State_And_Audit_Log_Without_Loss()
    {
        var factory = CreateConnectionFactory();
        var company = Guid.NewGuid();

        // Quatre documents couvrant les 4 états pré-V010 : 0=Pending, 1=Accepted, 2=TacitlyAccepted, 3=Contested.
        var docPending = Guid.NewGuid();
        var docAccepted = Guid.NewGuid();
        var docTacit = Guid.NewGuid();
        var docContested = Guid.NewGuid();

        var pendingSince = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var deadline = pendingSince.AddDays(30);
        var transitionAt = pendingSince.AddDays(1);
        var operatorId = Guid.NewGuid();
        var contestorId = Guid.NewGuid();

        using (var connection = await factory.OpenAsync())
        {
            // Semer mandats.self_billed_acceptances (schéma pré-V010 : avec colonnes state + deadline_utc).
            const string insertAcceptance = """
                INSERT INTO mandats.self_billed_acceptances
                    (company_id, document_id, state, allocated_number, pending_since, deadline_utc, created_at, updated_at)
                VALUES
                    (@CompanyId, @DocPending,   0, NULL,      @PendingSince, @Deadline, @PendingSince, NULL),
                    (@CompanyId, @DocAccepted,  1, 'ABC-001', @PendingSince, @Deadline, @PendingSince, @TransitionAt),
                    (@CompanyId, @DocTacit,     2, NULL,      @PendingSince, @Deadline, @PendingSince, @TransitionAt),
                    (@CompanyId, @DocContested, 3, NULL,      @PendingSince, @Deadline, @PendingSince, @TransitionAt)
                """;
            await connection.ExecuteAsync(insertAcceptance, new
            {
                CompanyId = company,
                DocPending = docPending,
                DocAccepted = docAccepted,
                DocTacit = docTacit,
                DocContested = docContested,
                PendingSince = pendingSince,
                Deadline = deadline,
                TransitionAt = transitionAt,
            });

            // Semer mandats.self_billed_acceptance_log : genèse pour chaque doc + transition pour les 3 non-Pending.
            const string insertLog = """
                INSERT INTO mandats.self_billed_acceptance_log
                    (company_id, document_id, from_state, to_state, operator_id, operator_name, occurred_at)
                VALUES
                    (@CompanyId, @DocPending,   NULL, 0, NULL,         'Ingestion (test)',     @PendingSince),
                    (@CompanyId, @DocAccepted,  NULL, 0, NULL,         'Ingestion (test)',     @PendingSince),
                    (@CompanyId, @DocTacit,     NULL, 0, NULL,         'Ingestion (test)',     @PendingSince),
                    (@CompanyId, @DocContested, NULL, 0, NULL,         'Ingestion (test)',     @PendingSince),
                    (@CompanyId, @DocAccepted,  0,    1, @OperatorId,  'Opérateur',            @TransitionAt),
                    (@CompanyId, @DocTacit,     0,    2, NULL,         'Bascule tacite (job)', @TransitionAt),
                    (@CompanyId, @DocContested, 0,    3, @ContestorId, 'Opérateur',            @TransitionAt)
                """;
            await connection.ExecuteAsync(insertLog, new
            {
                CompanyId = company,
                DocPending = docPending,
                DocAccepted = docAccepted,
                DocTacit = docTacit,
                DocContested = docContested,
                PendingSince = pendingSince,
                TransitionAt = transitionAt,
                OperatorId = operatorId,
                ContestorId = contestorId,
            });
        }

        // Appliquer V010 seule via un second run DbUp journalisé sur le même outbox.schema_versions.
        var v010Upgrader = DeployChanges.To
            .PostgresqlDatabase(ConnectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetAssembly(typeof(MandatsModuleRegistration))!,
                s => s.Contains(".Migrations.", StringComparison.Ordinal)
                     && s.Contains("V010", StringComparison.Ordinal))
            .JournalToPostgresqlTable("outbox", "schema_versions")
            .WithTransactionPerScript()
            .LogToNowhere()
            .Build();

        var v010Result = v010Upgrader.PerformUpgrade();
        v010Result.Successful.Should().BeTrue(
            because: $"V010 doit s'appliquer sans erreur : {v010Result.Error?.Message}");

        using var conn = await factory.OpenAsync();

        // ── document_validations : 4 lignes, états et attributs mappés correctement ──────────────
        var validations = (await conn.QueryAsync<ValidationRow>(
            """
            SELECT document_id       AS DocumentId,
                   state              AS State,
                   proof_level        AS ProofLevel,
                   express_acceptance_recorded AS ExpressAcceptanceRecorded,
                   deadline_utc       AS DeadlineUtc,
                   created_at         AS CreatedAt
            FROM documentapproval.document_validations
            WHERE company_id = @Company AND validation_purpose = 0
            """,
            new { Company = company })).ToList();

        validations.Should().HaveCount(4, because: "les 4 acceptances doivent avoir une ligne dans document_validations");

        var vPending = validations.Single(r => r.DocumentId == docPending);
        var vAccepted = validations.Single(r => r.DocumentId == docAccepted);
        var vTacit = validations.Single(r => r.DocumentId == docTacit);
        var vContested = validations.Single(r => r.DocumentId == docContested);

        // État : Pending→0, Accepted→2, TacitlyAccepted→3, Contested→5.
        vPending.State.Should().Be(0);
        vAccepted.State.Should().Be(2);
        vTacit.State.Should().Be(3);
        vContested.State.Should().Be(5);

        // proof_level : Accepted(1)/Tacit(2) → 1 (Recorded) ; autres → 0 (None).
        vPending.ProofLevel.Should().Be(0);
        vAccepted.ProofLevel.Should().Be(1);
        vTacit.ProofLevel.Should().Be(1);
        vContested.ProofLevel.Should().Be(0);

        // express_acceptance_recorded : true UNIQUEMENT pour Accepted(1).
        vPending.ExpressAcceptanceRecorded.Should().BeFalse();
        vAccepted.ExpressAcceptanceRecorded.Should().BeTrue();
        vTacit.ExpressAcceptanceRecorded.Should().BeFalse();
        vContested.ExpressAcceptanceRecorded.Should().BeFalse();

        // deadline_utc et created_at conservés (timestamptz → DateTime UTC).
        vAccepted.DeadlineUtc!.Value.Should().BeCloseTo(deadline.UtcDateTime, TimeSpan.FromSeconds(1));
        vPending.CreatedAt.Should().BeCloseTo(pendingSince.UtcDateTime, TimeSpan.FromSeconds(1));

        // ── document_approval_log : 7 lignes (4 genèses + 3 transitions) ──────────────────────────
        var logs = (await conn.QueryAsync<LogRow>(
            """
            SELECT document_id   AS DocumentId,
                   from_state    AS FromState,
                   to_state      AS ToState,
                   operator_id   AS OperatorId,
                   operator_name AS OperatorName,
                   occurred_at   AS OccurredAt
            FROM documentapproval.document_approval_log
            WHERE company_id = @Company AND validation_purpose = 0
            ORDER BY occurred_at, document_id
            """,
            new { Company = company })).ToList();

        logs.Should().HaveCount(7, because: "4 genèses + 3 transitions = 7 entrées de journal");

        // Genèse Accepted (FromState NULL, ToState=0).
        var genesisAccepted = logs.First(r => r.DocumentId == docAccepted && r.FromState is null);
        genesisAccepted.ToState.Should().Be(0);
        genesisAccepted.OccurredAt.Should().BeCloseTo(pendingSince.UtcDateTime, TimeSpan.FromSeconds(1));

        // Transition Accepted (FromState=0, ToState=2, OperatorName='Opérateur').
        var transAccepted = logs.Single(r => r.DocumentId == docAccepted && r.FromState == 0);
        transAccepted.ToState.Should().Be(2);
        transAccepted.OperatorName.Should().Be("Opérateur");
        transAccepted.OccurredAt.Should().BeCloseTo(transitionAt.UtcDateTime, TimeSpan.FromSeconds(1));

        // Transition Tacit (FromState=0, ToState=3, OperatorId NULL).
        var transTacit = logs.Single(r => r.DocumentId == docTacit && r.FromState == 0);
        transTacit.ToState.Should().Be(3);
        transTacit.OperatorId.Should().BeNull();

        // ── companion fiscale : 4 lignes préservées + allocated_number intact ──────────────────────
        var companions = (await conn.QueryAsync<CompanionRow>(
            """
            SELECT document_id       AS DocumentId,
                   allocated_number  AS AllocatedNumber,
                   pending_since     AS PendingSince
            FROM mandats.self_billed_acceptances
            WHERE company_id = @Company
            """,
            new { Company = company })).ToList();

        companions.Should().HaveCount(4, because: "la companion fiscale survit à V010");
        companions.Single(r => r.DocumentId == docAccepted).AllocatedNumber.Should().Be("ABC-001");
        companions.Single(r => r.DocumentId == docPending).PendingSince
            .Should().BeCloseTo(pendingSince.UtcDateTime, TimeSpan.FromSeconds(1));

        // ── colonnes state + deadline_utc supprimées de mandats.self_billed_acceptances ────────────
        var droppedColumnCount = await conn.ExecuteScalarAsync<long>(
            """
            SELECT count(*) FROM information_schema.columns
            WHERE table_schema = 'mandats'
              AND table_name   = 'self_billed_acceptances'
              AND column_name  IN ('state', 'deadline_utc')
            """);
        droppedColumnCount.Should().Be(0, because: "V010 doit supprimer les colonnes state et deadline_utc de la companion");

        // ── table self_billed_acceptance_log supprimée ────────────────────────────────────────────
        // to_regclass renvoie le type `regclass` (illisible en object via Npgsql) → cast ::text ; NULL si absente.
        var logTableName = await conn.ExecuteScalarAsync<string?>(
            "SELECT to_regclass('mandats.self_billed_acceptance_log')::text");
        logTableName.Should().BeNull(because: "V010 doit DROPper mandats.self_billed_acceptance_log");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────

    private NpgsqlConnectionFactory CreateConnectionFactory()
    {
        var options = Options.Create(new DatabaseOptions { ConnectionString = ConnectionString });
        return new NpgsqlConnectionFactory(options);
    }

    private void RunCommonMigrations()
    {
        var options = Options.Create(new DatabaseOptions { ConnectionString = ConnectionString });
        var migrationOptions = Options.Create(new MigrationAssembliesOptions());
        var runner = new MigrationRunner(options, migrationOptions, NullLogger<MigrationRunner>.Instance);
        runner.MigrateUp();
    }

    private void RunModuleMigrationsExceptV010()
    {
        // DocumentApproval AVANT Mandats (même ordre qu'en production). V010 exclus : appliqué sur données plus bas.
        var upgrader = DeployChanges.To
            .PostgresqlDatabase(ConnectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetAssembly(typeof(DocumentApprovalModuleRegistration))!,
                s => s.Contains(".Migrations.", StringComparison.Ordinal))
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetAssembly(typeof(MandatsModuleRegistration))!,
                s => s.Contains(".Migrations.", StringComparison.Ordinal)
                     && !s.Contains("V010", StringComparison.Ordinal))
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

    // ── Query POCOs ───────────────────────────────────────────────────────────────────────────────

    // Npgsql matérialise timestamptz en DateTime (Kind=Utc), pas DateTimeOffset : les POCOs Dapper portent donc
    // DateTime (sinon la résolution du constructeur record échoue — pas de ctor au type des colonnes).
    private sealed record ValidationRow(
        Guid DocumentId,
        int State,
        int ProofLevel,
        bool ExpressAcceptanceRecorded,
        DateTime? DeadlineUtc,
        DateTime CreatedAt);

    private sealed record LogRow(
        Guid DocumentId,
        int? FromState,
        int ToState,
        Guid? OperatorId,
        string OperatorName,
        DateTime OccurredAt);

    private sealed record CompanionRow(
        Guid DocumentId,
        string? AllocatedNumber,
        DateTime PendingSince);
}
