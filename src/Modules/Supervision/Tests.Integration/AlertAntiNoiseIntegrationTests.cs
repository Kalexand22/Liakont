namespace Liakont.Modules.Supervision.Tests.Integration;

using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Supervision.Application;
using Liakont.Modules.Supervision.Domain;
using Liakont.Modules.Supervision.Infrastructure;
using Liakont.Modules.Supervision.Tests.Integration.Doubles;
using Liakont.Modules.Supervision.Tests.Integration.Fixtures;
using Xunit;

/// <summary>
/// Anti-bruit et auto-résolution du moteur sur PostgreSQL réel : un déclenchement crée une alerte, un
/// second cycle de la même règle ne crée PAS de doublon (index unique partiel), la disparition de la
/// condition résout l'alerte, et un nouveau déclenchement après résolution crée une nouvelle alerte.
/// (INV-SUPERVISION-003, 004)
/// </summary>
[Collection("SupervisionIntegration")]
public sealed class AlertAntiNoiseIntegrationTests
{
    private const string Tenant = "acme";

    private readonly TenantDatabase _db;

    public AlertAntiNoiseIntegrationTests(SupervisionDatabaseFixture fixture)
    {
        _db = fixture.CreateTenantDatabase();
    }

    [Fact]
    public async Task Engine_RaisesOnce_Resolves_And_Refires_On_Real_Database()
    {
        var store = new PostgresAlertStore(_db.ConnectionFactory);
        var queries = new PostgresAlertQueries(_db.ConnectionFactory);
        var rule = new StubAlertRule("agent.mute", AlertSeverity.Critical, isFiring: true, detail: "muet");
        var engine = new AlertEvaluationService(new[] { rule }, store);

        // Cycle 1 : déclenchement → 1 alerte active.
        await engine.EvaluateAsync(Tenant);
        (await queries.ListActiveAsync()).Should().ContainSingle();

        // Cycle 2 : règle toujours déclenchée → anti-bruit, pas de doublon.
        await engine.EvaluateAsync(Tenant);
        (await queries.ListActiveAsync()).Should().ContainSingle();
        (await queries.ListRecentAsync(50)).Should().ContainSingle();

        // Cycle 3 : condition disparue → auto-résolution.
        rule.IsFiring = false;
        await engine.EvaluateAsync(Tenant);
        (await queries.ListActiveAsync()).Should().BeEmpty();

        // Cycle 4 : re-déclenchement après résolution → nouvelle alerte.
        rule.IsFiring = true;
        await engine.EvaluateAsync(Tenant);
        (await queries.ListActiveAsync()).Should().ContainSingle();
        (await queries.ListRecentAsync(50)).Should().HaveCount(2);
    }
}
