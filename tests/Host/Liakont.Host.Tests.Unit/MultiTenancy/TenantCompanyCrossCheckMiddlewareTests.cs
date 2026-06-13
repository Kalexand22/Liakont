namespace Liakont.Host.Tests.Unit.MultiTenancy;

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Agent.Contracts.Transport;
using Liakont.Host.MultiTenancy;
using Microsoft.AspNetCore.Http;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Abstractions.Security;
using Xunit;

/// <summary>
/// Cross-check d'isolation par claim company_id (RLM03, ADR-0021 §2b, INV-0021-4). Vérifie le caractère
/// fail-closed pour un utilisateur de tenant (absence de company_id, divergence, indice client contradictoire
/// ⇒ 403) ET les exemptions (anonyme, super-admin, chemin agent X-Agent-Key). Le cross-check ne touche jamais
/// la donnée : refus à la frontière. La couverture GLOBALE (route arbitraire, position pipeline) est prouvée
/// en intégration (TenantCompanyCrossCheckIntegrationTests).
/// </summary>
public sealed class TenantCompanyCrossCheckMiddlewareTests
{
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";

    private static readonly Guid CompanyA = new("aaaaaaaa-0000-0000-0000-0000000000a1");

    private bool _nextCalled;

    [Fact]
    public async Task An_anonymous_request_passes_through()
    {
        var context = BuildContext(authenticated: false);

        await InvokeAsync(context, servedTenant: TenantA, actorCompanyId: null);

        _nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task An_agent_request_with_X_Agent_Key_is_out_of_scope_even_if_authenticated()
    {
        var context = BuildContext(authenticated: true, agentKey: "agent-secret");

        // Même sans company_id : le chemin agent résout son tenant depuis la clé API scopée (hors périmètre).
        await InvokeAsync(context, servedTenant: TenantA, actorCompanyId: null);

        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task A_super_admin_without_company_id_is_exempt()
    {
        var context = BuildContext(authenticated: true, roles: ["SystemAdmin"]);

        await InvokeAsync(context, servedTenant: TenantA, actorCompanyId: null);

        _nextCalled.Should().BeTrue("l'opérateur d'instance accède en cross-tenant, sans company_id");
    }

    [Fact]
    public async Task A_tenant_user_without_company_id_is_rejected()
    {
        var context = BuildContext(authenticated: true);

        await InvokeAsync(context, servedTenant: TenantA, actorCompanyId: null);

        _nextCalled.Should().BeFalse("fail-closed : un utilisateur de tenant DOIT porter company_id");
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task A_company_id_that_maps_to_no_tenant_is_rejected()
    {
        var context = BuildContext(authenticated: true);

        await InvokeAsync(context, servedTenant: TenantA, actorCompanyId: CompanyA, lookup: new Dictionary<Guid, string>());

        _nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task A_served_tenant_that_differs_from_the_token_tenant_is_rejected()
    {
        var context = BuildContext(authenticated: true);

        // company A → tenant A, mais la requête est servie comme tenant B (un repli client a piloté la résolution).
        await InvokeAsync(context, servedTenant: TenantB, actorCompanyId: CompanyA, lookup: Map(CompanyA, TenantA));

        _nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task A_client_supplied_hint_that_contradicts_the_token_is_rejected()
    {
        var context = BuildContext(authenticated: true);

        // company A → tenant A (servi = A, cohérent), MAIS un indice client (header/sous-domaine) dit tenant B.
        await InvokeAsync(
            context,
            servedTenant: TenantA,
            actorCompanyId: CompanyA,
            lookup: Map(CompanyA, TenantA),
            clientHints: [TenantB]);

        _nextCalled.Should().BeFalse("un indice client-fourni contredisant le jeton ⇒ 403 (jamais servi en silence)");
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task A_client_supplied_hint_that_agrees_with_the_token_passes_through()
    {
        var context = BuildContext(authenticated: true);

        await InvokeAsync(
            context,
            servedTenant: TenantA,
            actorCompanyId: CompanyA,
            lookup: Map(CompanyA, TenantA),
            clientHints: [TenantA]);

        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task A_consistent_tenant_user_with_no_client_hint_passes_through()
    {
        var context = BuildContext(authenticated: true);

        // Sans aucun indice client : le tenant servi est celui DÉRIVÉ du jeton — 403 ne serait pas justifié.
        await InvokeAsync(context, servedTenant: TenantA, actorCompanyId: CompanyA, lookup: Map(CompanyA, TenantA));

        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task The_token_tenant_comparison_is_case_insensitive()
    {
        var context = BuildContext(authenticated: true);

        await InvokeAsync(context, servedTenant: "Tenant-A", actorCompanyId: CompanyA, lookup: Map(CompanyA, TenantA));

        _nextCalled.Should().BeTrue();
    }

    private static Dictionary<Guid, string> Map(Guid companyId, string tenantId) => new() { [companyId] = tenantId };

    private static DefaultHttpContext BuildContext(bool authenticated, string[]? roles = null, string? agentKey = null)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new System.IO.MemoryStream();

        if (agentKey is not null)
        {
            context.Request.Headers[AgentApiHeaders.AgentKey] = agentKey;
        }

        if (authenticated)
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "user-1") };
            foreach (var role in roles ?? [])
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        }

        return context;
    }

    private async Task InvokeAsync(
        HttpContext context,
        string? servedTenant,
        Guid? actorCompanyId,
        IReadOnlyDictionary<Guid, string>? lookup = null,
        IReadOnlyList<string?>? clientHints = null)
    {
        var middleware = new TenantCompanyCrossCheckMiddleware(_ =>
        {
            _nextCalled = true;
            return Task.CompletedTask;
        });

        var hints = new List<IClientSuppliedTenantResolver>();
        foreach (var hint in clientHints ?? [])
        {
            hints.Add(new FakeClientResolver(hint));
        }

        await middleware.InvokeAsync(
            context,
            new FakeTenantContext(servedTenant),
            new FakeActorContextAccessor(actorCompanyId),
            new FakeCompanyTenantLookup(lookup ?? new Dictionary<Guid, string>()),
            hints);
    }

    private sealed class FakeTenantContext : ITenantContext
    {
        public FakeTenantContext(string? tenantId) => TenantId = tenantId;

        public string? TenantId { get; }

        public bool IsResolved => TenantId is not null;
    }

    private sealed class FakeClientResolver : IClientSuppliedTenantResolver
    {
        private readonly string? _value;

        public FakeClientResolver(string? value) => _value = value;

        public string? Resolve() => _value;
    }

    private sealed class FakeCompanyTenantLookup : ICompanyTenantLookup
    {
        private readonly IReadOnlyDictionary<Guid, string> _map;

        public FakeCompanyTenantLookup(IReadOnlyDictionary<Guid, string> map) => _map = map;

        public string? FindTenantId(Guid companyId) => _map.TryGetValue(companyId, out var tenantId) ? tenantId : null;
    }

    private sealed class FakeActorContextAccessor : IActorContextAccessor
    {
        public FakeActorContextAccessor(Guid? companyId) => Current = new FakeActor(companyId);

        public IActorContext Current { get; }
    }

    private sealed class FakeActor : IActorContext
    {
        public FakeActor(Guid? companyId) => CompanyId = companyId;

        public Guid UserId => Guid.Empty;

        public Guid CorrelationId => Guid.Empty;

        public bool IsAuthenticated => true;

        public string? DisplayName => null;

        public string? Email => null;

        public Guid? CompanyId { get; }

        public string? Timezone => null;

        public string? Language => null;

        public string? TenantId => null;
    }
}
