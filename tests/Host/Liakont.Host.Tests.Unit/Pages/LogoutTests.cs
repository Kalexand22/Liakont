namespace Liakont.Host.Tests.Unit.Pages;

using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components.Pages.Auth;
using Liakont.Host.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

public sealed class LogoutTests : BunitContext
{
    private readonly RecordingAuthenticationService _authService = new();

    public LogoutTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
    }

    [Fact]
    public void Should_Redirect_To_Login_Without_SignOut_On_Get()
    {
        ConfigureKeycloak(active: false);

        Render<Logout>(ps => ps.AddCascadingValue<HttpContext>(BuildHttpContext(HttpMethods.Get)));

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.Uri.Should().EndWith("/login");

        // Une navigation GET directe sur /logout ne déconnecte PAS (anti CSRF de déconnexion).
        _authService.SignedOutSchemes.Should().BeEmpty();
    }

    [Fact]
    public void Should_Redirect_To_Oidc_Logout_On_Post_When_Keycloak_Active()
    {
        ConfigureKeycloak(active: true);

        Render<Logout>(ps => ps.AddCascadingValue<HttpContext>(BuildHttpContext(HttpMethods.Post)));

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.Uri.Should().EndWith("/auth/oidc-logout");

        // Le cookie reste en place : /auth/oidc-logout a besoin de l'id_token pour end_session.
        _authService.SignedOutSchemes.Should().BeEmpty();
    }

    [Fact]
    public void Should_SignOut_Cookie_And_Redirect_To_Login_On_Post_In_Legacy_Mode()
    {
        ConfigureKeycloak(active: false);

        Render<Logout>(ps => ps.AddCascadingValue<HttpContext>(BuildHttpContext(HttpMethods.Post)));

        _authService.SignedOutSchemes.Should().ContainSingle()
            .Which.Should().Be(CookieAuthenticationDefaults.AuthenticationScheme);
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.Uri.Should().EndWith("/login");
    }

    private void ConfigureKeycloak(bool active) =>
        Services.AddSingleton<IOptions<KeycloakSettings>>(Options.Create(new KeycloakSettings
        {
            Authority = active ? "http://localhost:8080/realms/liakont-dev" : string.Empty,
        }));

    private DefaultHttpContext BuildHttpContext(string method)
    {
        var requestServices = new ServiceCollection()
            .AddSingleton<IAuthenticationService>(_authService)
            .BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = requestServices };
        context.Request.Method = method;
        return context;
    }

    private sealed class RecordingAuthenticationService : IAuthenticationService
    {
        public List<string> SignedOutSchemes { get; } = [];

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
            SignedOutSchemes.Add(scheme ?? string.Empty);
            return Task.CompletedTask;
        }
    }
}
