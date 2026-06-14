namespace Liakont.Host.Tests.Unit.MultiTenancy;

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.MultiTenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.MultiTenancy;
using Xunit;

/// <summary>
/// Middleware d'application du statut Suspendu (OPS03.4 lot B) : requêtes anonymes et super-admins
/// jamais bloqués, tenant actif traversant, tenant suspendu → 403 français sur l'API et
/// signout + redirection sur l'UI. Les données ne sont jamais touchées (refus à la frontière).
/// </summary>
public sealed class TenantSuspensionMiddlewareTests
{
    private readonly RecordingAuthenticationService _authService = new();
    private bool _nextCalled;

    [Fact]
    public async Task An_Anonymous_Request_Passes_Through()
    {
        var context = BuildContext(authenticated: false, path: "/login");

        await Invoke(context, suspended: true);

        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task A_Super_Admin_Is_Never_Blocked()
    {
        var context = BuildContext(authenticated: true, path: "/admin/tenants", roles: ["SystemAdmin"]);

        await Invoke(context, suspended: true);

        _nextCalled.Should().BeTrue("l'opérateur d'instance doit pouvoir réactiver le tenant");
    }

    [Fact]
    public async Task An_Active_Tenant_Passes_Through()
    {
        var context = BuildContext(authenticated: true, path: "/documents");

        await Invoke(context, suspended: false);

        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task A_Suspended_Tenant_Gets_A_French_403_On_The_Api()
    {
        var context = BuildContext(authenticated: true, path: "/api/v1/documents");

        await Invoke(context, suspended: true);

        _nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task A_Suspended_Tenant_Is_Signed_Out_And_Redirected_On_The_Ui()
    {
        var context = BuildContext(authenticated: true, path: "/documents");

        await Invoke(context, suspended: true);

        _nextCalled.Should().BeFalse();
        _authService.SignedOut.Should().BeTrue("la session est fermée — pas une session zombie");
        context.Response.StatusCode.Should().Be(StatusCodes.Status302Found);
        context.Response.Headers.Location.ToString().Should().Be("/tenant-suspendu");
    }

    [Fact]
    public async Task A_Request_Without_Resolved_Tenant_Passes_Through()
    {
        var context = BuildContext(authenticated: true, path: "/documents");

        await InvokeWithTenant(context, tenantId: null, suspended: true);

        _nextCalled.Should().BeTrue("sans tenant résolu (endpoint système), aucun statut à appliquer");
    }

    private Task Invoke(HttpContext context, bool suspended) =>
        InvokeWithTenant(context, "acme", suspended);

    private async Task InvokeWithTenant(HttpContext context, string? tenantId, bool suspended)
    {
        var middleware = new TenantSuspensionMiddleware(_ =>
        {
            _nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, new FakeTenantContext(tenantId), new FakeLookup(suspended));
    }

    private DefaultHttpContext BuildContext(bool authenticated, string path, string[]? roles = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAuthenticationService>(_authService);
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Path = path;

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

    private sealed class FakeTenantContext : ITenantContext
    {
        public FakeTenantContext(string? tenantId) => TenantId = tenantId;

        public string? TenantId { get; }

        public bool IsResolved => TenantId is not null;
    }

    private sealed class FakeLookup : ITenantSuspensionLookup
    {
        private readonly bool _suspended;

        public FakeLookup(bool suspended) => _suspended = suspended;

        public Task<bool> IsSuspendedAsync(string tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_suspended);

        public void Invalidate(string tenantId)
        {
        }
    }

    private sealed class RecordingAuthenticationService : IAuthenticationService
    {
        public bool SignedOut { get; private set; }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            SignedOut = true;
            return Task.CompletedTask;
        }
    }
}
