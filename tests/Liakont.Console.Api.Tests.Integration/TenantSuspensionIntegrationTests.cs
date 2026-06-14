namespace Liakont.Console.Api.Tests.Integration;

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Liakont.Agent.Contracts.Transport;
using Liakont.Host.MultiTenancy;
using Liakont.Modules.Ingestion.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

/// <summary>
/// Application du statut SUSPENDU (OPS03.4 lot B), de bout en bout sur le tenant dédié
/// <see cref="ConsoleApiFactory.TenantSusp"/> : le PUSH agent (écriture) répond 403 avec un message
/// français distinct d'une clé révoquée, le HEARTBEAT reste servi (supervision continue, reprise
/// automatique), la CONSOLE répond 403 à un utilisateur du tenant (le SystemAdmin n'est jamais
/// bloqué), un tenant SANS profil reste accepté (jamais de suspension implicite), et la
/// RÉACTIVATION rétablit le push — données intactes pendant toute la séquence.
/// Le statut est muté en SQL + invalidation du cache du lookup (la console du lot C passera par
/// SetTenantStatusCommand + Invalidate, même mécanique).
/// </summary>
[Collection(ConsoleApiCollectionFixture.Name)]
public sealed class TenantSuspensionIntegrationTests : IAsyncLifetime
{
    private const string AgentsPath = "/api/v1/agents";
    private const string PdfPoolPath = "/api/agent/v1/pdf-pool?fileName=test.pdf";
    private const string HeartbeatPath = "/api/agent/v1/heartbeat";

    private readonly ConsoleApiFactory _factory;

    public TenantSuspensionIntegrationTests(ConsoleApiFactory factory)
    {
        _factory = factory;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>Chaque test rend le tenant ACTIF en sortant (aucune pollution des suites suivantes).</summary>
    public Task DisposeAsync() => SetStatutAsync(suspendu: false);

    [Fact]
    public async Task A_Suspended_Tenant_Refuses_Push_But_Keeps_Heartbeat_Then_Resumes_On_Reactivation()
    {
        var agentKey = await RegisterAgentKeyAsync();
        using var agent = AgentClient(agentKey);

        // Actif : le push passe.
        (await PushPdfAsync(agent)).Should().Be(HttpStatusCode.OK, "tenant actif — le push est accepté");

        // Suspendu : les TROIS endpoints d'écriture sont refusés 403 avec un message FRANÇAIS
        // distinct d'une clé révoquée…
        await SetStatutAsync(suspendu: true);
        var refused = await agent.PostAsync(PdfPoolPath, PdfContent());
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await refused.Content.ReadAsStringAsync();
        body.Should().Contain("suspendu").And.Contain("conservées", "le message distingue la suspension d'une clé révoquée");

        var refusedBatch = await agent.PostAsJsonAsync(
            "/api/agent/v1/documents/batch",
            new { contractVersion = AgentContractVersionPolicy.Current, documents = Array.Empty<object>() });
        refusedBatch.StatusCode.Should().Be(HttpStatusCode.Forbidden, "le lot de documents est une écriture");

        var refusedLinkedPdf = await agent.PostAsync("/api/agent/v1/documents/REF-001/pdf", PdfContent());
        refusedLinkedPdf.StatusCode.Should().Be(HttpStatusCode.Forbidden, "le PDF rattaché est une écriture");

        // …mais le HEARTBEAT reste servi : l'agent demeure supervisé et reprendra seul.
        var heartbeat = await agent.PostAsJsonAsync(
            HeartbeatPath,
            new { agentVersion = "1.0.0-test", sentAtUtc = DateTimeOffset.UtcNow });
        heartbeat.StatusCode.Should().Be(HttpStatusCode.OK, "la supervision continue pendant la suspension");

        // Réactivation : le push repasse — aucune donnée n'a été touchée entre-temps.
        await SetStatutAsync(suspendu: false);
        (await PushPdfAsync(agent)).Should().Be(HttpStatusCode.OK, "la réactivation rétablit le push sans intervention sur l'agent");
    }

    [Fact]
    public async Task A_User_Of_A_Suspended_Tenant_Gets_A_403_On_The_Console_But_The_SystemAdmin_Passes()
    {
        await SetStatutAsync(suspendu: true);

        // Utilisateur du tenant (lecteur seedé) : refusé en français.
        using var reader = _factory.CreateClient(ConsoleApiFactory.TenantSusp, ConsoleApiFactory.ReaderUserId);
        var refused = await reader.GetAsync("/api/v1/settings");
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("suspendu");

        // SystemAdmin : jamais bloqué (il doit pouvoir réactiver).
        using var admin = _factory.CreateClient(
            ConsoleApiFactory.TenantSusp, ConsoleApiFactory.SystemAdminUserId, roles: "SystemAdmin");
        var adminResponse = await admin.GetAsync($"/api/v1/admin/tenants/{ConsoleApiFactory.TenantSusp}");
        adminResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_Tenant_Without_Profile_Keeps_Accepting_Push()
    {
        // Tenant SANS profil (tenant-arch : identité seedée, aucun profil) : jamais de suspension
        // implicite — non-régression du parc existant (un tenant jamais seedé accepte ses agents).
        var agentKey = await RegisterAgentKeyAsync(ConsoleApiFactory.TenantArchive);
        using var agent = AgentClient(agentKey);

        (await PushPdfAsync(agent)).Should().Be(HttpStatusCode.OK);
    }

    private static StringContent PdfContent() =>
        new("%PDF-1.4 contenu de test", Encoding.UTF8, "application/pdf");

    private static async Task<HttpStatusCode> PushPdfAsync(HttpClient agent)
    {
        var response = await agent.PostAsync(PdfPoolPath, PdfContent());
        return response.StatusCode;
    }

    private HttpClient AgentClient(string agentKey)
    {
        var client = new HttpClient { BaseAddress = new Uri(_factory.BaseUrl) };
        client.DefaultRequestHeaders.Add(AgentApiHeaders.AgentKey, agentKey);
        client.DefaultRequestHeaders.Add(AgentApiHeaders.ContractVersion, AgentContractVersionPolicy.Current);
        return client;
    }

    /// <summary>Enregistre un agent (endpoint console réel, clé retournée une fois) sur le tenant donné.</summary>
    private async Task<string> RegisterAgentKeyAsync(string tenantId = ConsoleApiFactory.TenantSusp)
    {
        using var settings = _factory.CreateClient(tenantId, ConsoleApiFactory.SettingsUserId);
        var response = await settings.PostAsJsonAsync(AgentsPath, new { name = $"agent-susp-{Guid.NewGuid():N}" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var issued = await response.Content.ReadFromJsonAsync<IssuedKeyResponse>(
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return issued!.FullKey;
    }

    /// <summary>Mute le statut du profil (SQL) puis INVALIDE le cache du lookup (effet immédiat).</summary>
    private async Task SetStatutAsync(bool suspendu)
    {
        await using var conn = new NpgsqlConnection(_factory.TenantConnectionString(ConsoleApiFactory.TenantSusp));
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "UPDATE tenantsettings.tenant_profiles SET statut = @Statut",
            new { Statut = suspendu ? 1 : 0 });

        _factory.Services.GetRequiredService<ITenantSuspensionLookup>().Invalidate(ConsoleApiFactory.TenantSusp);
    }

    private sealed record IssuedKeyResponse(Guid AgentId, string FullKey);
}
