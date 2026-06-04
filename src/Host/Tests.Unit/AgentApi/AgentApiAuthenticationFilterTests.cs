namespace Liakont.Host.Tests.Unit.AgentApi;

using FluentAssertions;
using Liakont.Agent.Contracts.Transport;
using Liakont.Host.AgentApi;
using Liakont.Host.MultiTenancy;
using Liakont.Modules.Ingestion.Contracts.Authentication;
using Microsoft.AspNetCore.Http;
using Xunit;

/// <summary>
/// Couvre la logique propre du filtre d'authentification de l'API agent : négociation de version
/// (426), mapping des issues d'authentification (401/403), passage à l'endpoint et POSE du contexte
/// tenant sur succès (résolution réutilisée par PIV04). La résolution de clé elle-même est couverte
/// par les tests d'intégration du module Ingestion.
/// </summary>
public sealed class AgentApiAuthenticationFilterTests
{
    private static readonly object Sentinel = new();

    [Theory]
    [InlineData(null)]
    [InlineData("2")]
    [InlineData("inconnu")]
    public async Task Unsupported_Or_Missing_Contract_Version_Returns_426(string? versionHeader)
    {
        var (result, tenant) = await InvokeAsync(AgentAuthenticationResult.InvalidKey(), versionHeader);

        StatusCodeOf(result).Should().Be(StatusCodes.Status426UpgradeRequired);
        tenant.TenantId.Should().BeNull();
    }

    [Fact]
    public async Task Invalid_Key_Returns_401()
    {
        var (result, tenant) = await InvokeAsync(AgentAuthenticationResult.InvalidKey(), "1");

        StatusCodeOf(result).Should().Be(StatusCodes.Status401Unauthorized);
        tenant.TenantId.Should().BeNull();
    }

    [Fact]
    public async Task Revoked_Key_Returns_403()
    {
        var (result, tenant) = await InvokeAsync(AgentAuthenticationResult.Revoked(), "1");

        StatusCodeOf(result).Should().Be(StatusCodes.Status403Forbidden);
        tenant.TenantId.Should().BeNull();
    }

    [Fact]
    public async Task Valid_Key_Calls_Next_And_Sets_Tenant_Context()
    {
        var identity = new AgentIdentity(Guid.NewGuid(), "acme", "Poste 1");

        var (result, tenant) = await InvokeAsync(AgentAuthenticationResult.Authenticated(identity), "1");

        result.Should().BeSameAs(Sentinel, "le filtre laisse passer la requête vers l'endpoint.");
        tenant.TenantId.Should().Be("acme", "la résolution du tenant est posée (réutilisée par PIV04).");
    }

    private static async Task<(object? Result, MutableTenantContext Tenant)> InvokeAsync(
        AgentAuthenticationResult authResult, string? contractVersionHeader)
    {
        var tenant = new MutableTenantContext();
        var http = new DefaultHttpContext
        {
            RequestServices = new StubServiceProvider(new StubAuthenticator(authResult), tenant),
        };

        if (contractVersionHeader is not null)
        {
            http.Request.Headers[AgentApiHeaders.ContractVersion] = contractVersionHeader;
        }

        http.Request.Headers[AgentApiHeaders.AgentKey] = "agt_test.secret";

        var invocationContext = EndpointFilterInvocationContext.Create(http);
        var filter = new AgentApiAuthenticationFilter();

        var result = await filter.InvokeAsync(invocationContext, _ => ValueTask.FromResult<object?>(Sentinel));
        return (result, tenant);
    }

    private static int StatusCodeOf(object? result) =>
        ((IStatusCodeHttpResult)result!).StatusCode ?? 0;

    private sealed class StubServiceProvider : IServiceProvider
    {
        private readonly IAgentAuthenticator _authenticator;
        private readonly MutableTenantContext _tenant;

        public StubServiceProvider(IAgentAuthenticator authenticator, MutableTenantContext tenant)
        {
            _authenticator = authenticator;
            _tenant = tenant;
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IAgentAuthenticator))
            {
                return _authenticator;
            }

            if (serviceType == typeof(MutableTenantContext))
            {
                return _tenant;
            }

            return null;
        }
    }

    private sealed class StubAuthenticator : IAgentAuthenticator
    {
        private readonly AgentAuthenticationResult _result;

        public StubAuthenticator(AgentAuthenticationResult result)
        {
            _result = result;
        }

        public Task<AgentAuthenticationResult> AuthenticateAsync(string? presentedKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
    }
}
