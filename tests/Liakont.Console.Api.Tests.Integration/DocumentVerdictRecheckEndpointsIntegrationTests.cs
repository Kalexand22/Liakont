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
/// Tests d'intégration in-process des endpoints d'ACTION garde-fou B2B/B2C + re-vérification (API02b) :
/// <c>POST /documents/{id}/verdict</c> et <c>POST /documents/{id}/recheck</c>. Vérifie la permission
/// <c>liakont.actions</c> (401/403), les gardes (404 hors tenant/inexistant, 409 hors Blocked, 400 verdict
/// inconnu), la persistance + journalisation du verdict (marqueur B2C, événement, audit), le traitement
/// manuel (Blocked → ManuallyHandled), et la re-vérification : un acheteur à indice « société » reste
/// Blocked au recheck (garde-fou VAL05), puis — après verdict « confirmer B2C » — passe ReadyToSend (la
/// décision opérateur sourcée F08 §A.4 est incorporée à la validation, jamais un affaiblissement silencieux).
/// <para>
/// Opère sur le tenant DÉDIÉ <see cref="ConsoleApiFactory.TenantVerdict"/> (profil + table TVA validée part
/// Autre) : ces actions MUTENT l'état de documents, les confiner évite de polluer les comptes exacts d'API02a.
/// Chaque test seede son PROPRE document bloqué + pivot stagé (pas de couplage inter-tests).
/// </para>
/// </summary>
[Collection(ConsoleApiCollectionFixture.Name)]
public sealed class DocumentVerdictRecheckEndpointsIntegrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ConsoleApiFactory _factory;

    public DocumentVerdictRecheckEndpointsIntegrationTests(ConsoleApiFactory factory)
    {
        _factory = factory;
    }

    private static string VerdictPath(Guid id) => $"/api/v1/documents/{id}/verdict";

    private static string RecheckPath(Guid id) => $"/api/v1/documents/{id}/recheck";

    private static JsonContent Verdict(string verdict) => JsonContent.Create(new { verdict });

    [Fact]
    public async Task PostVerdict_Without_Authentication_Returns_401()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantVerdict);

        var response = await client.PostAsync(VerdictPath(Guid.NewGuid()), Verdict("confirm_b2c"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostVerdict_With_Read_Only_Permission_Returns_403()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantVerdict, ConsoleApiFactory.ReaderUserId);

        var response = await client.PostAsync(VerdictPath(Guid.NewGuid()), Verdict("confirm_b2c"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostVerdict_Unknown_Verdict_Returns_400()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantVerdict, ConsoleApiFactory.OperatorUserId);

        var response = await client.PostAsync(VerdictPath(Guid.NewGuid()), Verdict("n_importe_quoi"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostVerdict_NonExistent_Document_Returns_404()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantVerdict, ConsoleApiFactory.OperatorUserId);

        var response = await client.PostAsync(VerdictPath(Guid.NewGuid()), Verdict("confirm_b2c"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostVerdict_Document_Not_Blocked_Returns_409()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantVerdict, ConsoleApiFactory.OperatorUserId);

        var response = await client.PostAsync(VerdictPath(ConsoleApiFactory.TenantVerdictReadyDocId), Verdict("confirm_b2c"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PostVerdict_Other_Tenant_Document_Returns_404_Isolation()
    {
        // Le document bloqué du tenant d'action n'existe pas dans la base du tenant de verdict : 404 (tenant-scopé).
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantVerdict, ConsoleApiFactory.OperatorUserId);

        var response = await client.PostAsync(VerdictPath(ConsoleApiFactory.TenantActDocBlockedId), Verdict("confirm_b2c"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostVerdict_ConfirmB2c_Records_Decision_And_Stays_Blocked()
    {
        var documentId = await _factory.SeedBlockedProfessionalBuyerDocumentAsync(ConsoleApiFactory.TenantVerdict);
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantVerdict, ConsoleApiFactory.OperatorUserId);

        var response = await client.PostAsync(VerdictPath(documentId), Verdict("confirm_b2c"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<VerdictResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.Verdict.Should().Be("confirm_b2c");
        body.State.Should().Be("Blocked", "le verdict B2C ne change pas l'état (la re-vérification débloque ensuite)");

        // Décision persistée + journalisée (marqueur + événement d'audit + activité opérateur).
        (await _factory.GetDocumentStateAsync(ConsoleApiFactory.TenantVerdict, documentId)).Should().Be("Blocked");
        (await _factory.IsBuyerConfirmedAsync(ConsoleApiFactory.TenantVerdict, documentId)).Should().BeTrue();
        (await _factory.CountDocumentEventsAsync(ConsoleApiFactory.TenantVerdict, documentId, "DocumentBuyerConfirmedB2C"))
            .Should().Be(1, "le verdict B2C écrit un fait d'audit append-only");
        (await _factory.CountActivitiesAsync("documents.verdict_confirm_b2c", documentId.ToString()))
            .Should().BePositive("le verdict est journalisé avec l'identité de l'opérateur");
    }

    [Fact]
    public async Task PostVerdict_HandleManually_Transitions_To_ManuallyHandled()
    {
        var documentId = await _factory.SeedBlockedProfessionalBuyerDocumentAsync(ConsoleApiFactory.TenantVerdict);
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantVerdict, ConsoleApiFactory.OperatorUserId);

        var response = await client.PostAsync(VerdictPath(documentId), Verdict("handle_manually"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<VerdictResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.Verdict.Should().Be("handle_manually");
        body.State.Should().Be("ManuallyHandled");

        (await _factory.GetDocumentStateAsync(ConsoleApiFactory.TenantVerdict, documentId)).Should().Be("ManuallyHandled");
        (await _factory.CountDocumentEventsAsync(ConsoleApiFactory.TenantVerdict, documentId, "DocumentManuallyHandled"))
            .Should().Be(1, "le traitement manuel écrit un fait d'audit append-only");
        (await _factory.CountActivitiesAsync("documents.verdict_handle_manually", documentId.ToString()))
            .Should().BePositive("le traitement manuel est journalisé avec l'identité de l'opérateur");
    }

    [Fact]
    public async Task PostRecheck_With_Read_Only_Permission_Returns_403()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantVerdict, ConsoleApiFactory.ReaderUserId);

        var response = await client.PostAsync(RecheckPath(Guid.NewGuid()), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostRecheck_NonExistent_Document_Returns_404()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantVerdict, ConsoleApiFactory.OperatorUserId);

        var response = await client.PostAsync(RecheckPath(Guid.NewGuid()), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostRecheck_NonBlocked_Document_Returns_409()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantVerdict, ConsoleApiFactory.OperatorUserId);

        var response = await client.PostAsync(RecheckPath(ConsoleApiFactory.TenantVerdictReadyDocId), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PostRecheck_Other_Tenant_Document_Returns_404_Isolation()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantVerdict, ConsoleApiFactory.OperatorUserId);

        var response = await client.PostAsync(RecheckPath(ConsoleApiFactory.TenantActDocBlockedId), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostRecheck_With_Unavailable_Staged_Pivot_Returns_409()
    {
        // Chemin dégradé « bloquer plutôt qu'envoyer faux » : pivot stagé indisponible → 409, document reste Blocked.
        var documentId = await _factory.SeedBlockedDocumentWithoutStagedPivotAsync(ConsoleApiFactory.TenantVerdict);
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantVerdict, ConsoleApiFactory.OperatorUserId);

        var response = await client.PostAsync(RecheckPath(documentId), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _factory.GetDocumentStateAsync(ConsoleApiFactory.TenantVerdict, documentId))
            .Should().Be("Blocked", "un contenu indisponible ne transitionne jamais le document");
    }

    [Fact]
    public async Task PostRecheck_Without_Verdict_Stays_Blocked_With_Fresh_Motifs()
    {
        // Sans verdict opérateur, le garde-fou re-bloque : reste Blocked avec les nouveaux motifs renvoyés.
        var documentId = await _factory.SeedBlockedProfessionalBuyerDocumentAsync(ConsoleApiFactory.TenantVerdict);
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantVerdict, ConsoleApiFactory.OperatorUserId);

        var response = await client.PostAsync(RecheckPath(documentId), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<RecheckResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.State.Should().Be("Blocked");
        body.BlockingReason.Should().NotBeNullOrWhiteSpace().And.Contain("professionnel", "le motif frais du garde-fou est renvoyé pour affichage immédiat");

        // Aucune transition (Blocked → Blocked interdit) : le document reste bloqué.
        (await _factory.GetDocumentStateAsync(ConsoleApiFactory.TenantVerdict, documentId)).Should().Be("Blocked");
    }

    [Fact]
    public async Task PostRecheck_After_ConfirmB2c_Transitions_To_ReadyToSend()
    {
        var documentId = await _factory.SeedBlockedProfessionalBuyerDocumentAsync(ConsoleApiFactory.TenantVerdict);
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantVerdict, ConsoleApiFactory.OperatorUserId);

        // 1) Verdict « confirmer B2C » (la décision est incorporée à la re-validation).
        (await client.PostAsync(VerdictPath(documentId), Verdict("confirm_b2c"))).StatusCode.Should().Be(HttpStatusCode.OK);

        // 2) Re-vérification : le garde-fou ne bloque plus, le mapping + les autres règles passent → ReadyToSend.
        var response = await client.PostAsync(RecheckPath(documentId), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<RecheckResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.State.Should().Be("ReadyToSend");
        body.BlockingReason.Should().BeNull();

        // Transition réelle persistée + journalisée (état + événement ReadyToSend + activité de re-vérification).
        (await _factory.GetDocumentStateAsync(ConsoleApiFactory.TenantVerdict, documentId)).Should().Be("ReadyToSend");
        (await _factory.CountDocumentEventsAsync(ConsoleApiFactory.TenantVerdict, documentId, "DocumentReadyToSend"))
            .Should().Be(1, "le déblocage écrit un fait d'audit append-only");
        (await _factory.CountActivitiesAsync("documents.rechecked", documentId.ToString()))
            .Should().BePositive("la re-vérification est journalisée");
    }

    private sealed record VerdictResponse(Guid DocumentId, string Verdict, string State);

    private sealed record RecheckResponse(Guid DocumentId, string State, string? BlockingReason);
}
