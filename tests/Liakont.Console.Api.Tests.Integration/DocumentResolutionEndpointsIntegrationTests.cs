namespace Liakont.Console.Api.Tests.Integration;

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

/// <summary>
/// Tests d'intégration in-process des endpoints de RÉSOLUTION TERMINALE du module Documents pour la console
/// (API02c) : <c>POST /documents/{id}/resolve-manually</c> (Blocked/RejectedByPa → ManuallyHandled, motif
/// obligatoire) et <c>POST /documents/{id}/supersede</c> (RejectedByPa → Superseded, lien vers le remplaçant).
/// Vérifie la permission <c>liakont.actions</c> (401/403), les gardes (400 motif/remplaçant manquant ou
/// auto-référence, 404 inexistant / hors tenant, 409 état incompatible / remplaçant absent), l'application
/// RÉELLE de la transition (état + DocumentEvent append-only en base) et la journalisation (module Audit).
/// <para>
/// Chaque scénario MUTANT opère sur un document DÉDIÉ du tenant <see cref="ConsoleApiFactory.TenantAction"/>
/// (isolation de la fixture partagée) ; les scénarios en refus (4xx) n'appliquent aucune transition.
/// </para>
/// </summary>
[Collection(ConsoleApiCollectionFixture.Name)]
public sealed class DocumentResolutionEndpointsIntegrationTests
{
    private readonly ConsoleApiFactory _factory;

    public DocumentResolutionEndpointsIntegrationTests(ConsoleApiFactory factory)
    {
        _factory = factory;
    }

    private static string ResolvePath(Guid id) => $"/api/v1/documents/{id}/resolve-manually";

    private static string SupersedePath(Guid id) => $"/api/v1/documents/{id}/supersede";

    // ───────────────────────── resolve-manually ─────────────────────────
    [Fact]
    public async Task PostResolve_Without_Authentication_Returns_401()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantAction);

        var response = await client.PostAsJsonAsync(
            ResolvePath(ConsoleApiFactory.TenantActDocStableIssuedId), new { reason = "x" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostResolve_With_Read_Only_Permission_Returns_403()
    {
        // Un lecteur (liakont.read) ne peut pas exécuter une action (liakont.actions) — séparation des droits.
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantAction, ConsoleApiFactory.ReaderUserId);

        var response = await client.PostAsJsonAsync(
            ResolvePath(ConsoleApiFactory.TenantActDocStableIssuedId), new { reason = "x" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostResolve_Without_Reason_Returns_400()
    {
        // Motif obligatoire (journalisé, F06 §3) : un motif vide / espaces est refusé AVANT toute transition.
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantAction, ConsoleApiFactory.OperatorUserId);

        var response = await client.PostAsJsonAsync(
            ResolvePath(ConsoleApiFactory.TenantActDocStableIssuedId), new { reason = "   " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostResolve_NonExistent_Document_Returns_404()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantAction, ConsoleApiFactory.OperatorUserId);

        var response = await client.PostAsJsonAsync(ResolvePath(Guid.NewGuid()), new { reason = "perdu" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostResolve_Other_Tenant_Document_Returns_404_Isolation()
    {
        // Le document du tenant A n'existe pas dans la base du tenant d'action (écriture tenant-scopée) : 404.
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantAction, ConsoleApiFactory.OperatorUserId);

        var response = await client.PostAsJsonAsync(
            ResolvePath(ConsoleApiFactory.TenantADocBlockedId), new { reason = "hors tenant" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostResolve_Wrong_State_Returns_409()
    {
        // Un document Issued n'est ni Blocked ni RejectedByPa : pas de résolution manuelle.
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantAction, ConsoleApiFactory.OperatorUserId);

        var response = await client.PostAsJsonAsync(
            ResolvePath(ConsoleApiFactory.TenantActDocStableIssuedId), new { reason = "tentative" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _factory.GetDocumentStateAsync(ConsoleApiFactory.TenantAction, ConsoleApiFactory.TenantActDocStableIssuedId))
            .Should().Be("Issued", "un 409 n'applique aucune transition");
    }

    [Fact]
    public async Task PostResolve_Blocked_Document_Transitions_To_ManuallyHandled_And_Logs()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantAction, ConsoleApiFactory.OperatorUserId);

        var response = await client.PostAsJsonAsync(
            ResolvePath(ConsoleApiFactory.TenantActDocResolveBlockedId),
            new { reason = "Avoir orphelin traité en comptabilité." });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _factory.GetDocumentStateAsync(ConsoleApiFactory.TenantAction, ConsoleApiFactory.TenantActDocResolveBlockedId))
            .Should().Be("ManuallyHandled", "la résolution applique réellement la transition terminale");
        (await _factory.CountDocumentEventsAsync(ConsoleApiFactory.TenantAction, ConsoleApiFactory.TenantActDocResolveBlockedId, "DocumentManuallyHandled"))
            .Should().Be(1, "la transition est inscrite dans la piste d'audit append-only");
        (await _factory.CountActivitiesAsync("documents.resolved_manually", ConsoleApiFactory.TenantActDocResolveBlockedId.ToString()))
            .Should().BePositive("l'action est journalisée (module Audit) avec l'identité de l'opérateur");
    }

    [Fact]
    public async Task PostResolve_RejectedByPa_Document_Transitions_To_ManuallyHandled()
    {
        // L'autre source autorisée de ManuallyHandled : un document rejeté par la PA, non retransmissible.
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantAction, ConsoleApiFactory.OperatorUserId);

        var response = await client.PostAsJsonAsync(
            ResolvePath(ConsoleApiFactory.TenantActDocResolveRejectedId),
            new { reason = "Document non retransmissible, classé manuellement." });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _factory.GetDocumentStateAsync(ConsoleApiFactory.TenantAction, ConsoleApiFactory.TenantActDocResolveRejectedId))
            .Should().Be("ManuallyHandled");
    }

    // ───────────────────────── supersede ─────────────────────────
    [Fact]
    public async Task PostSupersede_With_Read_Only_Permission_Returns_403()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantAction, ConsoleApiFactory.ReaderUserId);

        var response = await client.PostAsJsonAsync(
            SupersedePath(ConsoleApiFactory.TenantActDocSupersedeRejectedId),
            new { replacementDocumentId = ConsoleApiFactory.TenantActDocStableIssuedId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostSupersede_Without_Replacement_Returns_400()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantAction, ConsoleApiFactory.OperatorUserId);

        var response = await client.PostAsJsonAsync(
            SupersedePath(ConsoleApiFactory.TenantActDocSupersedeRejectedId),
            new { replacementDocumentId = Guid.Empty });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostSupersede_Self_Reference_Returns_400()
    {
        // Un document ne peut pas se remplacer lui-même.
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantAction, ConsoleApiFactory.OperatorUserId);

        var response = await client.PostAsJsonAsync(
            SupersedePath(ConsoleApiFactory.TenantActDocSupersedeRejectedId),
            new { replacementDocumentId = ConsoleApiFactory.TenantActDocSupersedeRejectedId });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostSupersede_Wrong_State_Returns_409()
    {
        // Seul un document RejectedByPa peut être remplacé : un Issued est refusé (sans charger le remplaçant).
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantAction, ConsoleApiFactory.OperatorUserId);

        var response = await client.PostAsJsonAsync(
            SupersedePath(ConsoleApiFactory.TenantActDocStableIssuedId),
            new { replacementDocumentId = ConsoleApiFactory.TenantActDocResolveRejectedId });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PostSupersede_Replacement_Not_In_Tenant_Returns_409()
    {
        // Le remplaçant doit exister dans le tenant : un id inconnu est refusé, sans muter le document rejeté.
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantAction, ConsoleApiFactory.OperatorUserId);

        var response = await client.PostAsJsonAsync(
            SupersedePath(ConsoleApiFactory.TenantActDocSupersedeNoReplId),
            new { replacementDocumentId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _factory.GetDocumentStateAsync(ConsoleApiFactory.TenantAction, ConsoleApiFactory.TenantActDocSupersedeNoReplId))
            .Should().Be("RejectedByPa", "un remplaçant inexistant n'applique aucune transition");
    }

    [Fact]
    public async Task PostSupersede_NonExistent_Document_Returns_404()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantAction, ConsoleApiFactory.OperatorUserId);

        var response = await client.PostAsJsonAsync(
            SupersedePath(Guid.NewGuid()),
            new { replacementDocumentId = ConsoleApiFactory.TenantActDocStableIssuedId });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostSupersede_Without_Authentication_Returns_401()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantAction);

        var response = await client.PostAsJsonAsync(
            SupersedePath(ConsoleApiFactory.TenantActDocSupersedeNoReplId),
            new { replacementDocumentId = ConsoleApiFactory.TenantActDocStableIssuedId });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostSupersede_Links_RejectedByPa_To_Replacement_And_Logs()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantAction, ConsoleApiFactory.OperatorUserId);

        var response = await client.PostAsJsonAsync(
            SupersedePath(ConsoleApiFactory.TenantActDocSupersedeRejectedId),
            new { replacementDocumentId = ConsoleApiFactory.TenantActDocStableIssuedId });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _factory.GetDocumentStateAsync(ConsoleApiFactory.TenantAction, ConsoleApiFactory.TenantActDocSupersedeRejectedId))
            .Should().Be("Superseded", "le document rejeté est lié à son remplaçant et passe à l'état terminal");
        (await _factory.CountDocumentEventsAsync(ConsoleApiFactory.TenantAction, ConsoleApiFactory.TenantActDocSupersedeRejectedId, "DocumentSuperseded"))
            .Should().Be(1, "la transition est inscrite dans la piste d'audit append-only");
        (await _factory.CountActivitiesAsync("documents.superseded", ConsoleApiFactory.TenantActDocSupersedeRejectedId.ToString()))
            .Should().BePositive("l'action est journalisée (module Audit) avec l'identité de l'opérateur");

        // Le remplaçant n'est pas muté : il existait déjà, seul le document rejeté change d'état.
        (await _factory.GetDocumentStateAsync(ConsoleApiFactory.TenantAction, ConsoleApiFactory.TenantActDocStableIssuedId))
            .Should().Be("Issued");
    }
}
