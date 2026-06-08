namespace Liakont.Console.Api.Tests.Integration;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// Garde-fou INV-IDN01-6 (ADR-0017) : le chemin base <c>/auth/test-login</c> (harnais non-OIDC) n'est PAS
/// orphelinisé par le passage de la garde endpoint aux claims. Il doit TOUJOURS émettre des claims
/// <c>permission</c> corrects et NON vides (issus des grants en base, <c>identity.grants</c>) — anti
/// faux-vert : un harnais silencieusement cassé masquerait toute la surface permission-gated des E2E.
/// </summary>
[Collection(ConsoleApiCollectionFixture.Name)]
public sealed class TestLoginPermissionClaimsIntegrationTests
{
    private const string TestLoginPath = "/auth/test-login";
    private const string PermissionClaimType = "permission";
    private const string SessionCookieName = "stratum_session";
    private const string LiakontReadPermission = "liakont.read";
    private const string LiakontActionsPermission = "liakont.actions";

    private readonly ConsoleApiFactory _factory;

    public TestLoginPermissionClaimsIntegrationTests(ConsoleApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task TestLogin_Should_Emit_NonEmpty_Permission_Claims_For_Operator()
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(_factory.BaseUrl) };
        client.DefaultRequestHeaders.Add("X-Tenant-Id", ConsoleApiFactory.TenantA);

        using var form = new FormUrlEncodedContent(
            [new KeyValuePair<string, string>("username", "console.operator")]);
        using var response = await client.PostAsync(TestLoginPath, form);

        // Sign-in réussi : redirection (302) + pose du cookie de session.
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var cookieValue = ExtractSessionCookie(response);
        cookieValue.Should().NotBeNullOrEmpty("test-login doit poser le cookie de session après sign-in");

        // Déchiffre le ticket du cookie et inspecte ses claims : test-login projette les permissions
        // de l'utilisateur (grants DB) en claims "permission" — le MÊME transport que la garde lit.
        var principal = UnprotectCookiePrincipal(cookieValue!);
        var permissions = principal.FindAll(PermissionClaimType).Select(c => c.Value).ToList();

        permissions.Should().NotBeEmpty("le chemin base ne doit pas être orphelinisé (INV-IDN01-6)");
        permissions.Should().Contain([LiakontReadPermission, LiakontActionsPermission]);
    }

    private static string? ExtractSessionCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            return null;
        }

        foreach (var setCookie in setCookies)
        {
            var firstSegment = setCookie.Split(';', 2)[0];
            var separator = firstSegment.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var name = firstSegment[..separator];
            if (string.Equals(name, SessionCookieName, StringComparison.Ordinal))
            {
                return firstSegment[(separator + 1)..];
            }
        }

        return null;
    }

    private ClaimsPrincipal UnprotectCookiePrincipal(string cookieValue)
    {
        var options = _factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

        var ticket = options.TicketDataFormat.Unprotect(cookieValue);
        ticket.Should().NotBeNull("le ticket de cookie doit être déchiffrable par le même format que l'hôte");
        return ticket!.Principal;
    }
}
