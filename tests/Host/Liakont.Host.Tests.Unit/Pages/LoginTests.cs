namespace Liakont.Host.Tests.Unit.Pages;

using System.Security.Claims;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components.Pages.Auth;
using Liakont.Host.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

public sealed class LoginTests : BunitContext
{
    public LoginTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddLocalization();
    }

    [Fact]
    public void Should_Render_Local_Form_When_Keycloak_Inactive()
    {
        ConfigureKeycloak(active: false);

        var cut = Render<Login>(ps => ps.AddCascadingValue<HttpContext>(AnonymousHttpContext()));

        cut.FindAll("[data-testid='login-username']").Should().ContainSingle();
        cut.FindAll("[data-testid='login-password']").Should().ContainSingle();
        cut.FindAll("[data-testid='login-submit']").Should().ContainSingle();

        // Le formulaire local poste vers l'endpoint de test, jamais vers Keycloak.
        cut.Find("form").GetAttribute("action").Should().Be("/auth/test-login");
    }

    [Fact]
    public void Should_Redirect_To_Oidc_Login_When_Keycloak_Active()
    {
        ConfigureKeycloak(active: true);

        Render<Login>(ps => ps.AddCascadingValue<HttpContext>(AnonymousHttpContext()));

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.Uri.Should().Contain("/auth/oidc-login");
    }

    [Fact]
    public void Should_Redirect_Home_When_Already_Authenticated()
    {
        ConfigureKeycloak(active: false);

        Render<Login>(ps => ps.AddCascadingValue<HttpContext>(AuthenticatedHttpContext()));

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.Uri.Should().Be(nav.BaseUri);
    }

    [Fact]
    public void Should_Sanitize_Absolute_ReturnUrl_To_Root_When_Already_Authenticated()
    {
        ConfigureKeycloak(active: false);
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/login?returnUrl=" + Uri.EscapeDataString("https://evil.example/"));

        Render<Login>(ps => ps.AddCascadingValue<HttpContext>(AuthenticatedHttpContext()));

        // Anti open-redirect : un returnUrl absolu est ramené à "/".
        nav.Uri.Should().Be(nav.BaseUri);
    }

    [Fact]
    public void Should_Render_Nothing_Actionable_Without_HttpContext()
    {
        ConfigureKeycloak(active: false);

        // Sans HttpContext (rendu hors SSR statique), la page n'affiche pas le formulaire local.
        var cut = Render<Login>();

        cut.FindAll("[data-testid='login-submit']").Should().BeEmpty();
    }

    private static DefaultHttpContext AnonymousHttpContext() => new();

    private static DefaultHttpContext AuthenticatedHttpContext() => new()
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "operateur")],
            authenticationType: "test")),
    };

    private void ConfigureKeycloak(bool active) =>
        Services.AddSingleton<IOptions<KeycloakSettings>>(Options.Create(new KeycloakSettings
        {
            Authority = active ? "http://localhost:8080/realms/liakont-dev" : string.Empty,
        }));
}
