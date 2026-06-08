namespace Liakont.Modules.Supervision.Tests.Integration;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Liakont.Modules.Documents.Infrastructure.Queries;
using Liakont.Modules.Ingestion.Contracts.DTOs;
using Liakont.Modules.Supervision.Application;
using Liakont.Modules.Supervision.Application.Rules;
using Liakont.Modules.Supervision.Infrastructure;
using Liakont.Modules.Supervision.Tests.Integration.Doubles;
using Liakont.Modules.Supervision.Tests.Integration.Fixtures;
using Liakont.Modules.TenantSettings.Infrastructure.Queries;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Infrastructure.Jobs;
using Xunit;

/// <summary>
/// Règles SUP01b sur PostgreSQL RÉEL : chaque règle lit une donnée DÉJÀ persistée via le Contract du module
/// propriétaire (Documents, TenantSettings) et pilote le moteur + le store d'alertes réels (déclenchement,
/// anti-bruit, auto-résolution). Prouve aussi la surcharge de seuil par tenant et l'isolation cross-tenant.
/// (INV-SUPERVISION-010, 011, 012)
/// </summary>
[Collection("SupervisionIntegration")]
public sealed class SupervisionRulesIntegrationTests
{
    private const string Tenant = "acme";

    private readonly SupervisionDatabaseFixture _fixture;

    public SupervisionRulesIntegrationTests(SupervisionDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task BlockedRule_Raises_Then_AutoResolves_On_Real_Database()
    {
        var db = _fixture.CreateRulesTenantDatabase();
        await SeedDocumentAsync(db, "Blocked", DateTimeOffset.UtcNow.AddDays(-6), "F-BLK-1");

        var store = new PostgresAlertStore(db.ConnectionFactory);
        var queries = new PostgresAlertQueries(db.ConnectionFactory);
        var engine = new AlertEvaluationService(new IAlertRule[] { BlockedRule(db) }, store);

        await engine.EvaluateAsync(Tenant);
        var active = await queries.ListActiveAsync();
        active.Should().ContainSingle(a => a.RuleKey == "documents.blocked")
            .Which.Severity.Should().Be("Warning");

        // Anti-bruit : un second cycle ne crée pas de doublon.
        await engine.EvaluateAsync(Tenant);
        (await queries.ListActiveAsync()).Should().ContainSingle(a => a.RuleKey == "documents.blocked");

        // La condition disparaît (document pris en charge) → auto-résolution.
        await SetDocumentStateAsync(db, "F-BLK-1", "ManuallyHandled");
        await engine.EvaluateAsync(Tenant);
        (await queries.ListActiveAsync()).Should().NotContain(a => a.RuleKey == "documents.blocked");
    }

    [Fact]
    public async Task PaRejectedRule_Raises_On_Real_Database()
    {
        var db = _fixture.CreateRulesTenantDatabase();
        await SeedDocumentAsync(db, "RejectedByPa", DateTimeOffset.UtcNow.AddDays(-3), "F-REJ-1");

        var store = new PostgresAlertStore(db.ConnectionFactory);
        var queries = new PostgresAlertQueries(db.ConnectionFactory);
        var engine = new AlertEvaluationService(new IAlertRule[] { PaRejectedRule(db) }, store);

        await engine.EvaluateAsync(Tenant);

        (await queries.ListActiveAsync()).Should().ContainSingle(a => a.RuleKey == "documents.pa_rejected")
            .Which.Severity.Should().Be("Critical");
    }

    [Fact]
    public async Task Tenant_Threshold_Override_Suppresses_Alert_On_Real_Database()
    {
        // Seuil tenant à 30 j : un document bloqué depuis 6 j ne déclenche PAS, là où le défaut produit (5 j)
        // déclencherait — prouve que la surcharge par tenant (CFG02) est réellement lue et appliquée.
        var db = _fixture.CreateRulesTenantDatabase();
        await SeedThresholdsAsync(db, Guid.NewGuid(), blockedDocumentsDays: 30);
        await SeedDocumentAsync(db, "Blocked", DateTimeOffset.UtcNow.AddDays(-6), "F-BLK-OVR");

        var store = new PostgresAlertStore(db.ConnectionFactory);
        var queries = new PostgresAlertQueries(db.ConnectionFactory);
        var engine = new AlertEvaluationService(new IAlertRule[] { BlockedRule(db) }, store);

        await engine.EvaluateAsync(Tenant);

        (await queries.ListActiveAsync()).Should().NotContain(a => a.RuleKey == "documents.blocked");
    }

    [Fact]
    public async Task AgentMuteRule_Raises_Then_AutoResolves_On_Real_Database()
    {
        // Source agent stubbée (PostgresAgentQueries est interne à Ingestion) ; le reste est réel : seuils via
        // PostgresTenantSettingsQueries (défaut 24 h faute de seuils seedés) + store d'alertes PostgreSQL.
        var db = _fixture.CreateRulesTenantDatabase();
        var store = new PostgresAlertStore(db.ConnectionFactory);
        var queries = new PostgresAlertQueries(db.ConnectionFactory);
        var agents = new StubAgentQueries(MuteAgent("Poste de vente", DateTimeOffset.UtcNow.AddHours(-25)));
        var rule = new AgentMuteAlertRule(agents, new PostgresTenantSettingsQueries(db.ConnectionFactory));
        var engine = new AlertEvaluationService(new IAlertRule[] { rule }, store);

        await engine.EvaluateAsync(Tenant);
        (await queries.ListActiveAsync()).Should().ContainSingle(a => a.RuleKey == "agent.mute")
            .Which.Severity.Should().Be("Critical");

        // L'agent reprend ses heartbeats → auto-résolution.
        agents.Set(MuteAgent("Poste de vente", DateTimeOffset.UtcNow));
        await engine.EvaluateAsync(Tenant);
        (await queries.ListActiveAsync()).Should().NotContain(a => a.RuleKey == "agent.mute");
    }

    [Fact]
    public async Task Blocked_Alerts_Are_Isolated_Per_Tenant()
    {
        // Le VRAI TenantJobRunner (SOL06) parcourt 2 bases tenant réelles ; seul alpha a un document bloqué.
        var alphaDb = _fixture.CreateRulesTenantDatabase();
        var betaDb = _fixture.CreateRulesTenantDatabase();
        await SeedDocumentAsync(alphaDb, "Blocked", DateTimeOffset.UtcNow.AddDays(-6), "F-A-1");

        var engines = new Dictionary<string, IAlertEvaluationService>
        {
            ["alpha"] = new AlertEvaluationService(new IAlertRule[] { BlockedRule(alphaDb) }, new PostgresAlertStore(alphaDb.ConnectionFactory)),
            ["beta"] = new AlertEvaluationService(new IAlertRule[] { BlockedRule(betaDb) }, new PostgresAlertStore(betaDb.ConnectionFactory)),
        };

        var runner = new TenantJobRunner(
            new ListTenantQueries(ListTenantQueries.ActiveTenant("alpha"), ListTenantQueries.ActiveTenant("beta")),
            new MapTenantScopeFactory(engines),
            NullLogger<TenantJobRunner>.Instance);

        var summary = await runner.RunForAllTenantsAsync(new SupervisionEvaluationTenantJob());
        summary.SucceededCount.Should().Be(2);

        var alphaAlerts = await new PostgresAlertQueries(alphaDb.ConnectionFactory).ListActiveAsync();
        var betaAlerts = await new PostgresAlertQueries(betaDb.ConnectionFactory).ListActiveAsync();
        alphaAlerts.Should().ContainSingle(a => a.RuleKey == "documents.blocked").Which.TenantId.Should().Be("alpha");
        betaAlerts.Should().BeEmpty("aucun document bloqué dans la base de beta — aucune fuite cross-tenant.");
    }

    private static BlockedDocumentsAlertRule BlockedRule(TenantDatabase db) =>
        new(new PostgresDocumentQueries(db.ConnectionFactory), new PostgresTenantSettingsQueries(db.ConnectionFactory));

    private static PaRejectedDocumentsAlertRule PaRejectedRule(TenantDatabase db) =>
        new(new PostgresDocumentQueries(db.ConnectionFactory), new PostgresTenantSettingsQueries(db.ConnectionFactory));

    private static AgentSummaryDto MuteAgent(string name, DateTimeOffset lastSeenAtUtc) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            KeyPrefix = "lkk_test",
            IsRevoked = false,
            CreatedAt = lastSeenAtUtc.AddDays(-30),
            LastSeenAtUtc = lastSeenAtUtc,
            LastAgentVersion = "1.0.0",
        };

    private static async Task SeedDocumentAsync(TenantDatabase db, string state, DateTimeOffset lastUpdateUtc, string number)
    {
        using var conn = await db.ConnectionFactory.OpenAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO documents.documents
                (id, source_reference, document_number, document_type, issue_date,
                 total_net, total_tax, total_gross, state, payload_hash, first_seen_utc, last_update_utc)
            VALUES
                (@Id, @Src, @Num, 'Invoice', @IssueDate,
                 100, 20, 120, @State, @Hash, @Ts, @Ts)
            """,
            new
            {
                Id = Guid.NewGuid(),
                Src = "SRC-" + number,
                Num = number,
                IssueDate = new DateOnly(2026, 1, 1),
                State = state,
                Hash = "hash-" + number,
                Ts = lastUpdateUtc,
            });
    }

    private static async Task SetDocumentStateAsync(TenantDatabase db, string number, string newState)
    {
        using var conn = await db.ConnectionFactory.OpenAsync();
        await conn.ExecuteAsync(
            "UPDATE documents.documents SET state = @State WHERE document_number = @Num",
            new { State = newState, Num = number });
    }

    private static async Task SeedThresholdsAsync(TenantDatabase db, Guid companyId, int blockedDocumentsDays)
    {
        using var conn = await db.ConnectionFactory.OpenAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO tenantsettings.tenant_profiles
                (company_id, siren, raison_sociale, address_street, address_postal_code, address_city, address_country)
            VALUES (@CompanyId, '111111111', 'Société Fictive', '1 rue de Test', '35000', 'Rennes', 'FR')
            """,
            new { CompanyId = companyId });

        await conn.ExecuteAsync(
            """
            INSERT INTO tenantsettings.alert_thresholds
                (company_id, agent_silent_hours, missed_run_hours, push_queue_max_items,
                 push_queue_max_age_hours, blocked_documents_days, pa_rejections_days, alert_tenant_contact)
            VALUES (@CompanyId, 24, 36, 50, 6, @BlockedDays, 2, false)
            """,
            new { CompanyId = companyId, BlockedDays = blockedDocumentsDays });
    }
}
