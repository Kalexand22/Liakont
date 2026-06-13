namespace Liakont.Console.Api.Tests.Integration;

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Agent.Contracts.Transport;
using Xunit;

/// <summary>
/// Tests d'intégration in-process du cross-check d'isolation par claim <c>company_id</c> (RLM03,
/// ADR-0021 §2b, INV-0021-4), exercé à travers le VRAI pipeline (<c>AppBootstrap.ConfigureMiddleware</c>).
/// Prouve la COUVERTURE GLOBALE (une route arbitraire non décorée est cross-checkée ⇒ ce n'est pas un filtre
/// d'endpoint ni un <c>[Authorize]</c>), le 403-sur-contradiction (jeton tenant A + en-tête tenant B), le
/// fail-closed (utilisateur de tenant sans <c>company_id</c>), et les exemptions (super-admin, chemin agent).
/// </summary>
[Collection(ConsoleApiCollectionFixture.Name)]
public sealed class TenantCompanyCrossCheckIntegrationTests
{
    private const string DocumentsPath = "/api/v1/documents";

    /// <summary>Route /api SANS endpoint (non décorée) : un 403 ici ne peut venir que d'un middleware GLOBAL.</summary>
    private const string UndecoratedProbePath = "/api/v1/__rlm03-cross-check-probe__";

    private readonly ConsoleApiFactory _factory;

    public TenantCompanyCrossCheckIntegrationTests(ConsoleApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task A_token_company_that_contradicts_an_undecorated_route_is_rejected_globally()
    {
        // Jeton tenant A (company A) + en-tête X-Tenant-Id tenant B, sur une route SANS endpoint : la voie
        // jeton est autoritaire (servi = A), l'indice client (header B) contredit ⇒ 403 GLOBAL. Un filtre
        // d'endpoint / [Authorize] laisserait une route inexistante en 404 — le 403 prouve la couverture globale.
        using var client = _factory.CreateClient(
            ConsoleApiFactory.TenantB, ConsoleApiFactory.ReaderUserId, ConsoleApiFactory.TenantACompanyId);

        var response = await client.GetAsync(UndecoratedProbePath);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_token_company_that_contradicts_a_real_endpoint_is_rejected()
    {
        using var client = _factory.CreateClient(
            ConsoleApiFactory.TenantB, ConsoleApiFactory.ReaderUserId, ConsoleApiFactory.TenantACompanyId);

        var response = await client.GetAsync(DocumentsPath);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_tenant_user_without_company_id_is_rejected()
    {
        // Utilisateur de tenant authentifié SANS claim company_id (on contourne le défaut de CreateClient) :
        // fail-closed ⇒ 403, AVANT toute autorisation.
        using var client = new HttpClient { BaseAddress = new Uri(_factory.BaseUrl) };
        client.DefaultRequestHeaders.Add("X-Tenant-Id", ConsoleApiFactory.TenantA);
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ConsoleApiFactory.ReaderUserId.ToString());

        var response = await client.GetAsync(DocumentsPath);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_consistent_tenant_user_is_served_from_the_token_tenant()
    {
        // Cas POSITIF : company A + tenant A cohérents (le défaut de CreateClient pose company A). Le tenant est
        // résolu DEPUIS le jeton, la requête est servie (200) — preuve que le 403 du test de contradiction est
        // « pour la bonne raison » (l'indice client B), pas un refus systématique.
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.ReaderUserId);

        var response = await client.GetAsync(DocumentsPath);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_super_admin_without_company_id_is_exempt_on_an_undecorated_route()
    {
        // Super-admin (SystemAdmin) SANS company_id, sur une route sans endpoint : exempté ⇒ pas de 403 du
        // cross-check (la route inexistante retombe en 404). Prouve l'exemption opérateur, globalement.
        using var client = _factory.CreateClient(
            ConsoleApiFactory.TenantA, ConsoleApiFactory.SystemAdminUserId, roles: "SystemAdmin");

        var response = await client.GetAsync(UndecoratedProbePath);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_agent_request_with_X_Agent_Key_is_out_of_scope()
    {
        // Requête portant X-Agent-Key (chemin agent) SANS company_id : hors périmètre du cross-check ⇒ jamais
        // 403 du cross-check (la route sans endpoint retombe en 404). Le chemin agent résout son tenant depuis
        // la clé API scopée (couvert par AgentApiAuthenticationFilter sur les endpoints agent réels).
        using var client = new HttpClient { BaseAddress = new Uri(_factory.BaseUrl) };
        client.DefaultRequestHeaders.Add(AgentApiHeaders.AgentKey, "agent-key-probe");

        var response = await client.GetAsync(UndecoratedProbePath);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }
}
