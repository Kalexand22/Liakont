namespace Liakont.Console.Api.Tests.Integration;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

/// <summary>
/// Tests d'intégration in-process des endpoints de RÉCONCILIATION PDF (API04), sur le harness HTTP de la
/// console (API01a). Vérifie : permission <c>liakont.actions</c> (lecture seule / sans droit → 403, anonyme
/// → 401), la file d'attente (propositions / orphelins / documents sans PDF), l'affichage d'un PDF,
/// la confirmation et le REJET d'une proposition, le lien manuel, et l'isolation tenant. Les MUTATIONS
/// portent sur le tenant dédié (<see cref="ConsoleApiFactory.TenantApi04"/>) avec des entrées de file
/// FRAÎCHES par test (aucune interférence inter-tests).
/// </summary>
[Collection(ConsoleApiCollectionFixture.Name)]
public sealed class ReconciliationEndpointsIntegrationTests
{
    private const string BasePath = "/api/v1/reconciliation";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ConsoleApiFactory _factory;

    public ReconciliationEndpointsIntegrationTests(ConsoleApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetQueue_Without_Authentication_Returns_401()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantApi04);
        var response = await client.GetAsync(BasePath);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetQueue_Without_Actions_Permission_Returns_403()
    {
        // Un lecteur (liakont.read, sans liakont.actions) ne peut PAS accéder à la file de réconciliation.
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantApi04, ConsoleApiFactory.ReaderUserId);
        var response = await client.GetAsync(BasePath);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetQueue_As_Actions_User_Lists_Proposal_And_Orphan()
    {
        var proposal = await _factory.SeedReconciliationProposalAsync(ConsoleApiFactory.TenantApi04);
        var orphan = await _factory.SeedReconciliationOrphanAsync(ConsoleApiFactory.TenantApi04);

        using var client = ActionsClient();
        var queue = await GetQueueAsync(client);

        queue.Proposals.Should().Contain(p => p.QueueEntryId == proposal.EntryId && p.ProposedDocumentId == proposal.DocumentId);
        queue.Orphans.Should().Contain(o => o.QueueEntryId == orphan.EntryId);
    }

    [Fact]
    public async Task GetPdf_As_Actions_User_Streams_The_Pdf()
    {
        var orphan = await _factory.SeedReconciliationOrphanAsync(ConsoleApiFactory.TenantApi04);

        using var client = ActionsClient();
        var response = await client.GetAsync($"{BasePath}/{orphan.EntryId}/pdf");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("inline", "le PDF s'affiche dans la console, il n'est pas téléchargé (WEB08)");
        var body = await response.Content.ReadAsByteArrayAsync();
        body.Should().Equal(orphan.Bytes);
    }

    [Fact]
    public async Task GetPdf_Unknown_Entry_Returns_404()
    {
        using var client = ActionsClient();
        var response = await client.GetAsync($"{BasePath}/{Guid.NewGuid()}/pdf");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RejectProposal_As_Actions_User_Reclasses_As_Orphan()
    {
        var proposal = await _factory.SeedReconciliationProposalAsync(ConsoleApiFactory.TenantApi04);

        using var client = ActionsClient();
        var reject = await client.PostAsync($"{BasePath}/{proposal.EntryId}/reject", content: null);
        reject.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var queue = await GetQueueAsync(client);
        queue.Proposals.Should().NotContain(p => p.QueueEntryId == proposal.EntryId, "la proposition rejetée n'est plus en attente");
        queue.Orphans.Should().Contain(o => o.QueueEntryId == proposal.EntryId, "le PDF rejeté redevient un orphelin (API04)");
    }

    [Fact]
    public async Task ConfirmProposal_As_Actions_User_Resolves_The_Entry()
    {
        var proposal = await _factory.SeedReconciliationProposalAsync(ConsoleApiFactory.TenantApi04);

        using var client = ActionsClient();
        var confirm = await client.PostAsync($"{BasePath}/{proposal.EntryId}/confirm", content: null);
        confirm.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var queue = await GetQueueAsync(client);
        queue.Proposals.Should().NotContain(p => p.QueueEntryId == proposal.EntryId, "la proposition confirmée est rapprochée");
    }

    [Fact]
    public async Task ConfirmProposal_Unknown_Entry_Returns_404()
    {
        using var client = ActionsClient();
        var response = await client.PostAsync($"{BasePath}/{Guid.NewGuid()}/confirm", content: null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task LinkPdf_As_Actions_User_Links_Orphan_To_Document()
    {
        var orphan = await _factory.SeedReconciliationOrphanAsync(ConsoleApiFactory.TenantApi04);
        var documentNumber = "FA-LINK-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var documentId = await _factory.ArchiveSampleDocumentAsync(
            ConsoleApiFactory.TenantApi04, documentNumber, new DateOnly(2026, 5, 1));

        using var client = ActionsClient();
        var link = await client.PostAsJsonAsync($"{BasePath}/link", new { queueEntryId = orphan.EntryId, documentId });
        link.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var queue = await GetQueueAsync(client);
        queue.Orphans.Should().NotContain(o => o.QueueEntryId == orphan.EntryId, "l'orphelin lié manuellement est rapproché");
    }

    [Fact]
    public async Task Reconciliation_Is_Tenant_Scoped()
    {
        // Une proposition seedée dans le tenant API04 n'apparaît JAMAIS dans la file d'un autre tenant.
        var proposal = await _factory.SeedReconciliationProposalAsync(ConsoleApiFactory.TenantApi04);

        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.OperatorUserId);
        var queue = await GetQueueAsync(client);

        queue.Proposals.Should().NotContain(p => p.QueueEntryId == proposal.EntryId);
    }

    private static async Task<QueueResponse> GetQueueAsync(HttpClient client)
    {
        var response = await client.GetAsync(BasePath);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var queue = await response.Content.ReadFromJsonAsync<QueueResponse>(JsonOptions);
        return queue!;
    }

    private HttpClient ActionsClient() =>
        _factory.CreateClient(ConsoleApiFactory.TenantApi04, ConsoleApiFactory.OperatorUserId);

    private sealed record QueueResponse(
        List<ProposalDto> Proposals,
        List<OrphanDto> Orphans,
        List<DocumentWithoutPdfDto> DocumentsWithoutPdf);

    private sealed record ProposalDto(Guid QueueEntryId, string PoolPdfId, string FileName, Guid ProposedDocumentId, string Strategy, string Confidence, string Detail);

    private sealed record OrphanDto(Guid QueueEntryId, string PoolPdfId, string FileName, string Detail);

    private sealed record DocumentWithoutPdfDto(Guid DocumentId, string DocumentNumber, decimal TotalGross);
}
