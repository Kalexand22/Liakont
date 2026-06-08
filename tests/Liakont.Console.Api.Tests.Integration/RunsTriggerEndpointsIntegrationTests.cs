namespace Liakont.Console.Api.Tests.Integration;

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

/// <summary>
/// Tests d'intégration in-process de l'endpoint d'ACTION <c>POST /runs/trigger</c> (API02a) : déclenchement
/// MANUEL du traitement du tenant courant. Vérifie la permission <c>liakont.actions</c> (401/403), la
/// publication tenant-scopée sur la queue SYSTÈME (<see cref="SendTenantTrigger"/>, jamais d'orphelin tenant
/// ni de fan-out), le mode simulation (<c>?dryRun=true</c>) et la journalisation de l'action (Audit).
/// </summary>
[Collection(ConsoleApiCollectionFixture.Name)]
public sealed class RunsTriggerEndpointsIntegrationTests
{
    private const string TriggerPath = "/api/v1/runs/trigger";
    private const string TenantTriggerType = "SendTenantTrigger";
    private const string AllTriggerType = "SendAllTrigger";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ConsoleApiFactory _factory;

    public RunsTriggerEndpointsIntegrationTests(ConsoleApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostRunsTrigger_Without_Authentication_Returns_401()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA);

        var response = await client.PostAsync(TriggerPath, content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostRunsTrigger_With_Read_Only_Permission_Returns_403()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.ReaderUserId);

        var response = await client.PostAsync(TriggerPath, content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostRunsTrigger_As_Operator_Publishes_TenantSend_On_System_Queue_And_Logs()
    {
        var beforeSystem = await _factory.CountSystemJobsAsync(TenantTriggerType);
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.OperatorUserId);

        var response = await client.PostAsync(TriggerPath, content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<RunTriggered>(JsonOptions);
        body.Should().NotBeNull();
        body!.JobId.Should().NotBeEmpty();
        body.DryRun.Should().BeFalse();

        (await _factory.CountSystemJobsAsync(TenantTriggerType)).Should().Be(beforeSystem + 1, "le déclenchement manuel publie un SendTenantTrigger sur la queue système");
        (await _factory.CountTenantJobsAsync(ConsoleApiFactory.TenantA, TenantTriggerType)).Should().Be(0, "aucun job orphelin en base tenant");
        (await _factory.CountSystemJobsAsync(AllTriggerType)).Should().Be(0, "aucun fan-out tous-tenants (INV-API02a-4)");
        (await _factory.CountActivitiesAsync("pipeline.run_triggered", "manual-trigger"))
            .Should().BePositive("le déclenchement manuel est journalisé");
    }

    [Fact]
    public async Task PostRunsTrigger_DryRun_Is_Accepted_As_Simulation()
    {
        var beforeSystem = await _factory.CountSystemJobsAsync(TenantTriggerType);
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.OperatorUserId);

        var response = await client.PostAsync($"{TriggerPath}?dryRun=true", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<RunTriggered>(JsonOptions);
        body.Should().NotBeNull();
        body!.DryRun.Should().BeTrue("le mode simulation est porté par la charge utile du déclencheur");
        (await _factory.CountSystemJobsAsync(TenantTriggerType)).Should().Be(beforeSystem + 1, "même en simulation, un job tenant-scopé est publié sur la queue système");
    }

    private sealed record RunTriggered(Guid JobId, bool DryRun);
}
