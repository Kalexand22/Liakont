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
/// Tests d'intégration in-process des endpoints d'ACTION du module Documents pour la console (API02a) :
/// <c>POST /documents/{id}/send</c> et <c>POST /documents/send-all</c>. Vérifie la permission
/// <c>liakont.actions</c> (401/403), les gardes (404 hors tenant / inexistant, 409 hors ReadyToSend), le
/// récapitulatif de confirmation (nombre + montant, sans exécution), la journalisation (Audit), et SURTOUT
/// la correction d'ADR-0016 : la publication se fait sur la queue SYSTÈME (<see cref="SendTenantTrigger"/>),
/// JAMAIS en base tenant (pas de job orphelin) ni en fan-out tous-tenants (pas de <c>SendAllTrigger</c>).
/// <para>
/// Les actions opèrent sur le tenant DÉDIÉ <see cref="ConsoleApiFactory.TenantAction"/> : un envoi déclenche
/// un SEND réel (consommé par le JobWorker live) qui écrit des <c>run_logs</c> ; les confiner évite de polluer
/// les comptes exacts qu'API01b asserte sur les tenants A/B.
/// </para>
/// </summary>
[Collection(ConsoleApiCollectionFixture.Name)]
public sealed class DocumentActionsEndpointsIntegrationTests
{
    private const string SendAllPath = "/api/v1/documents/send-all";
    private const string TenantTriggerType = "SendTenantTrigger";
    private const string AllTriggerType = "SendAllTrigger";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ConsoleApiFactory _factory;

    public DocumentActionsEndpointsIntegrationTests(ConsoleApiFactory factory)
    {
        _factory = factory;
    }

    private static string SendPath(Guid id) => $"/api/v1/documents/{id}/send";

    [Fact]
    public async Task PostSend_Without_Authentication_Returns_401()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantAction);

        var response = await client.PostAsync(SendPath(ConsoleApiFactory.TenantActDocReadyId), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostSend_With_Read_Only_Permission_Returns_403()
    {
        // Un lecteur (liakont.read) ne peut pas exécuter une action (liakont.actions) — séparation des droits.
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantAction, ConsoleApiFactory.ReaderUserId);

        var response = await client.PostAsync(SendPath(ConsoleApiFactory.TenantActDocReadyId), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostSend_NonExistent_Document_Returns_404()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantAction, ConsoleApiFactory.OperatorUserId);

        var response = await client.PostAsync(SendPath(Guid.NewGuid()), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostSend_Document_Not_ReadyToSend_Returns_409()
    {
        // Le document bloqué n'est pas envoyable : 409 (et message opérateur en français).
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantAction, ConsoleApiFactory.OperatorUserId);

        var response = await client.PostAsync(SendPath(ConsoleApiFactory.TenantActDocBlockedId), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PostSend_Other_Tenant_Document_Returns_404_Isolation()
    {
        // Le document du tenant A n'existe pas dans la base du tenant d'action (lecture tenant-scopée) : 404.
        var before = await _factory.CountSystemJobsAsync(TenantTriggerType);
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantAction, ConsoleApiFactory.OperatorUserId);

        var response = await client.PostAsync(SendPath(ConsoleApiFactory.TenantADocReadyId), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _factory.CountSystemJobsAsync(TenantTriggerType)).Should().Be(before, "un 404 ne publie aucun job");
    }

    [Fact]
    public async Task PostSend_Ready_Document_Publishes_TenantSend_On_System_Queue_And_Logs()
    {
        var beforeSystem = await _factory.CountSystemJobsAsync(TenantTriggerType);
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantAction, ConsoleApiFactory.OperatorUserId);

        var response = await client.PostAsync(SendPath(ConsoleApiFactory.TenantActDocReadyId), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<SendAccepted>(JsonOptions);
        body.Should().NotBeNull();
        body!.JobId.Should().NotBeEmpty();
        body.DocumentId.Should().Be(ConsoleApiFactory.TenantActDocReadyId);

        // ADR-0016 / INV-API02a-2 : publié sur la queue SYSTÈME (consommée par le worker), pas en base tenant.
        (await _factory.CountSystemJobsAsync(TenantTriggerType)).Should().Be(beforeSystem + 1, "l'envoi publie un SendTenantTrigger sur la queue système");
        (await _factory.CountTenantJobsAsync(ConsoleApiFactory.TenantAction, TenantTriggerType)).Should().Be(0, "aucun job orphelin en base tenant");

        // INV-API02a-4 : jamais de déclencheur fan-out tous-tenants depuis une action de console.
        (await _factory.CountSystemJobsAsync(AllTriggerType)).Should().Be(0, "aucun fan-out tous-tenants");

        // Journalisation de l'action (anti faux-vert : l'écriture d'audit est awaitée par l'endpoint).
        (await _factory.CountActivitiesAsync("documents.send_triggered", ConsoleApiFactory.TenantActDocReadyId.ToString()))
            .Should().BePositive("l'action d'envoi est journalisée avec l'identité de l'opérateur");
    }

    [Fact]
    public async Task PostSendAll_With_Read_Only_Permission_Returns_403()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantAction, ConsoleApiFactory.ReaderUserId);

        var response = await client.PostAsync(SendAllPath, content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostSendAll_Confirm_False_Returns_Recap_Without_Publishing()
    {
        // ?confirm=false (défaut) : récapitulatif (nombre + montant total) du SEUL tenant courant, sans exécuter.
        var before = await _factory.CountSystemJobsAsync(TenantTriggerType);
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantAction, ConsoleApiFactory.OperatorUserId);

        var response = await client.PostAsync($"{SendAllPath}?confirm=false", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var recap = await response.Content.ReadFromJsonAsync<SendAllSummary>(JsonOptions);
        recap.Should().NotBeNull();
        recap!.ConfirmationRequired.Should().BeTrue();
        recap.Count.Should().Be(1, "le tenant d'action a exactement un document ReadyToSend seedé");
        recap.TotalGross.Should().Be(100.00m, "montant TTC du seul ReadyToSend (decimal — jamais float)");
        recap.JobId.Should().BeNull();
        (await _factory.CountSystemJobsAsync(TenantTriggerType)).Should().Be(before, "un récapitulatif ne publie aucun job");
    }

    [Fact]
    public async Task PostSendAll_Confirm_True_Publishes_TenantSend_And_Logs()
    {
        var beforeSystem = await _factory.CountSystemJobsAsync(TenantTriggerType);
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantAction, ConsoleApiFactory.OperatorUserId);

        var response = await client.PostAsync($"{SendAllPath}?confirm=true", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var recap = await response.Content.ReadFromJsonAsync<SendAllSummary>(JsonOptions);
        recap.Should().NotBeNull();
        recap!.ConfirmationRequired.Should().BeFalse();
        recap.Count.Should().Be(1);
        recap.TotalGross.Should().Be(100.00m);
        recap.JobId.Should().NotBeNull().And.NotBe(Guid.Empty);

        (await _factory.CountSystemJobsAsync(TenantTriggerType)).Should().Be(beforeSystem + 1, "l'envoi groupé confirmé publie un SendTenantTrigger sur la queue système");
        (await _factory.CountTenantJobsAsync(ConsoleApiFactory.TenantAction, TenantTriggerType)).Should().Be(0, "aucun job orphelin en base tenant");
        (await _factory.CountSystemJobsAsync(AllTriggerType)).Should().Be(0, "aucun fan-out tous-tenants (INV-API02a-4)");
        (await _factory.CountActivitiesAsync("documents.send_all_triggered", "send-all"))
            .Should().BePositive("l'envoi groupé est journalisé");
    }

    private sealed record SendAccepted(Guid JobId, Guid DocumentId);

    private sealed record SendAllSummary(bool ConfirmationRequired, int Count, decimal TotalGross, Guid? JobId);
}
