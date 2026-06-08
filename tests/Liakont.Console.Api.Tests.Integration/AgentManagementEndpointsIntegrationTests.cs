namespace Liakont.Console.Api.Tests.Integration;

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Agent.Contracts.Transport;
using Liakont.Modules.Ingestion.Contracts;
using Liakont.Modules.Ingestion.Contracts.DTOs;
using Xunit;

/// <summary>
/// Tests d'intégration in-process des endpoints de GESTION DES AGENTS de la console (API05) :
/// <c>GET/POST /api/v1/agents</c>, <c>POST /agents/{id}/revoke</c>, <c>POST /agents/{id}/rotate-key</c>.
/// Vérifie la permission <c>liakont.settings</c> (401/403), la restitution UNIQUE de la clé à l'émission
/// (jamais en liste), la journalisation (Audit), l'isolation tenant (404 hors tenant), et SURTOUT — de
/// bout en bout contre la VRAIE API d'ingestion <c>/api/agent/v1</c> — que :
/// <list type="bullet">
///   <item>une clé RÉVOQUÉE est refusée à l'ingestion (403, l'agent existe mais est révoqué) ;</item>
///   <item>après ROTATION, l'ancienne clé est rejetée IMMÉDIATEMENT (401, son préfixe a disparu du
///         registre) tandis que la nouvelle est acceptée — aucune fenêtre de recouvrement (F12 §4.2,
///         CLAUDE.md n°3).</item>
/// </list>
/// Les agents vivent dans le registre SYSTÈME, clés par slug de tenant ; aucune autre suite ne lit
/// <c>ingestion.agents</c>, les assertions portent sur des identifiants précis (jamais des comptes exacts).
/// </summary>
[Collection(ConsoleApiCollectionFixture.Name)]
public sealed class AgentManagementEndpointsIntegrationTests
{
    private const string BasePath = "/api/v1/agents";

    // Endpoint d'ingestion authentifié le plus simple : une clé inconnue sur cette route EXISTANTE répond
    // 200 + Pending (ADR-0012), jamais 404 — l'authentification (filtre) tranche AVANT le handler, ce qui
    // en fait une sonde idéale du verdict d'auth (200 = acceptée, 401 = inconnue/rotée, 403 = révoquée).
    private const string AgentStatusProbePath = "/api/agent/v1/documents/status?sourceReference=probe&payloadHash=probe";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ConsoleApiFactory _factory;

    public AgentManagementEndpointsIntegrationTests(ConsoleApiFactory factory)
    {
        _factory = factory;
    }

    // ─────────────────────────── Permissions ───────────────────────────
    [Fact]
    public async Task GetAgents_Without_Authentication_Returns_401()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA);

        var response = await client.GetAsync(BasePath);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAgents_With_Read_Only_Permission_Returns_403()
    {
        // La gestion des agents = gestion de secrets : liakont.read seul ne suffit pas (CLAUDE.md n°10).
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.ReaderUserId);

        var response = await client.GetAsync(BasePath);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RegisterAgent_With_Actions_Only_Permission_Returns_403()
    {
        // Un opérateur « actions » ne gère pas les clés d'agent (séparation des droits : liakont.settings requis).
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.OperatorUserId);

        var response = await client.PostAsJsonAsync(BasePath, new { name = "Agent refusé" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ─────────────────────── Enregistrement + liste ───────────────────────
    [Fact]
    public async Task RegisterAgent_Returns_Full_Key_Once_And_List_Never_Exposes_The_Secret()
    {
        using var client = SettingsClient(ConsoleApiFactory.TenantA);

        var issued = await RegisterAgentAsync(client, "Agent Comptoir Lyon");

        issued.AgentId.Should().NotBeEmpty();
        issued.KeyPrefix.Should().NotBeNullOrWhiteSpace();
        issued.FullKey.Should().NotBeNullOrWhiteSpace();
        issued.FullKey.Should().StartWith(issued.KeyPrefix, "la clé complète est de la forme prefix.secret");

        // La liste expose l'agent SANS aucune clé : la part secrète de la clé complète ne doit JAMAIS
        // apparaître dans le corps (le préfixe public, lui, est attendu — il identifie la clé sans authentifier).
        var listResponse = await client.GetAsync(BasePath);
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var rawBody = await listResponse.Content.ReadAsStringAsync();
        var secretPart = issued.FullKey.Split('.', 2)[1];
        rawBody.Should().NotContain(secretPart, "la clé n'est restituée qu'à l'émission, jamais en liste (F12 §4.2)");
        rawBody.Should().NotContain("fullKey", "le DTO de liste n'a pas de champ clé complète");
        rawBody.ToLowerInvariant().Should().NotContain("keyhash", "l'empreinte de la clé n'est jamais exposée");

        var agents = JsonSerializer.Deserialize<AgentSummaryDto[]>(rawBody, JsonOptions)!;
        var mine = agents.Single(a => a.Id == issued.AgentId);
        mine.Name.Should().Be("Agent Comptoir Lyon");
        mine.IsRevoked.Should().BeFalse();
        mine.KeyPrefix.Should().Be(issued.KeyPrefix);

        // L'enregistrement est journalisé avec l'identité de l'opérateur (anti faux-vert : awaité avant la réponse).
        var auditCount = await _factory.CountActivitiesAsync("agents.registered", issued.AgentId.ToString());
        auditCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RegisterAgent_Without_Name_Returns_400()
    {
        using var client = SettingsClient(ConsoleApiFactory.TenantA);

        var response = await client.PostAsJsonAsync(BasePath, new { name = "   " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ──────────────────── Révocation (de bout en bout) ────────────────────
    [Fact]
    public async Task RevokeAgent_Then_Ingestion_With_Its_Key_Is_Rejected()
    {
        using var client = SettingsClient(ConsoleApiFactory.TenantA);
        var issued = await RegisterAgentAsync(client, "Agent à révoquer");

        // La clé est valide AVANT révocation (sinon le rejet aval ne prouverait rien — anti faux-vert).
        (await ProbeIngestionAsync(issued.FullKey)).Should().Be(HttpStatusCode.OK);

        var revoke = await client.PostAsync($"{BasePath}/{issued.AgentId}/revoke", content: null);
        revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var auditCount = await _factory.CountActivitiesAsync("agents.revoked", issued.AgentId.ToString());
        auditCount.Should().BeGreaterThan(0);

        // Révoquée : l'agent existe toujours (préfixe connu) mais sa clé est refusée → 403 (le filtre
        // d'ingestion distingue « révoqué » (403) d'« inconnu » (401) ; les deux sont rejetés à l'ingestion).
        (await ProbeIngestionAsync(issued.FullKey)).Should().Be(HttpStatusCode.Forbidden);

        var agents = await ListAgentsAsync(client);
        agents.Single(a => a.Id == issued.AgentId).IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task RevokeAgent_Unknown_Id_Returns_404()
    {
        using var client = SettingsClient(ConsoleApiFactory.TenantA);

        var response = await client.PostAsync($"{BasePath}/{Guid.NewGuid()}/revoke", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ──────────────── Rotation : invalidation immédiate (F12 §4.2) ────────────────
    [Fact]
    public async Task RotateKey_Old_Key_Rejected_Immediately_And_New_Key_Accepted()
    {
        using var client = SettingsClient(ConsoleApiFactory.TenantA);
        var first = await RegisterAgentAsync(client, "Agent à pivoter");

        (await ProbeIngestionAsync(first.FullKey)).Should().Be(HttpStatusCode.OK);

        var rotateResponse = await client.PostAsync($"{BasePath}/{first.AgentId}/rotate-key", content: null);
        rotateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotated = (await rotateResponse.Content.ReadFromJsonAsync<AgentKeyIssuedDto>(JsonOptions))!;
        rotated.AgentId.Should().Be(first.AgentId);
        rotated.FullKey.Should().NotBe(first.FullKey, "la rotation émet une clé entièrement nouvelle");
        rotated.KeyPrefix.Should().NotBe(first.KeyPrefix);

        var auditCount = await _factory.CountActivitiesAsync("agents.key_rotated", first.AgentId.ToString());
        auditCount.Should().BeGreaterThan(0);

        // Aucune fenêtre de recouvrement : l'ANCIENNE clé est rejetée immédiatement (son préfixe n'existe
        // plus dans le registre → 401), la NOUVELLE est acceptée (200). CLAUDE.md n°3.
        (await ProbeIngestionAsync(first.FullKey)).Should().Be(HttpStatusCode.Unauthorized);
        (await ProbeIngestionAsync(rotated.FullKey)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RotateKey_On_Revoked_Agent_Returns_409()
    {
        using var client = SettingsClient(ConsoleApiFactory.TenantA);
        var issued = await RegisterAgentAsync(client, "Agent révoqué puis piloté");

        (await client.PostAsync($"{BasePath}/{issued.AgentId}/revoke", content: null)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        var rotate = await client.PostAsync($"{BasePath}/{issued.AgentId}/rotate-key", content: null);

        rotate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ─────────────────────────── Isolation tenant ───────────────────────────
    [Fact]
    public async Task Agents_Are_Tenant_Isolated()
    {
        using var clientA = SettingsClient(ConsoleApiFactory.TenantA);
        using var clientB = SettingsClient(ConsoleApiFactory.TenantB);

        var issuedInA = await RegisterAgentAsync(clientA, "Agent du tenant A");

        // Le tenant B ne voit pas l'agent de A...
        var agentsB = await ListAgentsAsync(clientB);
        agentsB.Should().NotContain(a => a.Id == issuedInA.AgentId);

        // ...et ne peut pas le révoquer (introuvable dans son périmètre → 404, jamais d'action cross-tenant).
        var crossRevoke = await clientB.PostAsync($"{BasePath}/{issuedInA.AgentId}/revoke", content: null);
        crossRevoke.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // L'agent reste actif côté A (la tentative depuis B n'a rien touché).
        var agentsA = await ListAgentsAsync(clientA);
        agentsA.Single(a => a.Id == issuedInA.AgentId).IsRevoked.Should().BeFalse();
    }

    // ─────────────────────────────── Helpers ───────────────────────────────
    private static async Task<AgentKeyIssuedDto> RegisterAgentAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(BasePath, new { name });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<AgentKeyIssuedDto>(JsonOptions))!;
    }

    private static async Task<AgentSummaryDto[]> ListAgentsAsync(HttpClient client)
    {
        var response = await client.GetAsync(BasePath);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<AgentSummaryDto[]>(JsonOptions))!;
    }

    private HttpClient SettingsClient(string tenant) =>
        _factory.CreateClient(tenant, ConsoleApiFactory.SettingsUserId);

    /// <summary>
    /// Présente une clé d'agent à la VRAIE API d'ingestion (<c>/api/agent/v1</c>) et renvoie le code de
    /// statut : prouve le verdict d'authentification de bout en bout (200 acceptée / 401 inconnue ou rotée /
    /// 403 révoquée). En-têtes du contrat F12 §3.1 ; version courante négociée (sinon 426).
    /// </summary>
    private async Task<HttpStatusCode> ProbeIngestionAsync(string fullKey)
    {
        using var agentClient = new HttpClient { BaseAddress = new Uri(_factory.BaseUrl) };
        agentClient.DefaultRequestHeaders.Add(AgentApiHeaders.AgentKey, fullKey);
        agentClient.DefaultRequestHeaders.Add(AgentApiHeaders.ContractVersion, AgentContractVersionPolicy.Current);

        var response = await agentClient.GetAsync(AgentStatusProbePath);
        return response.StatusCode;
    }
}
