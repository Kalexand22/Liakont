namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Modules.Identity.Application.Preferences;
using Xunit;
using PreferencesPage = Liakont.Host.Components.Pages.Settings.UserPreferences;

public sealed class UserPreferencesPageTests : BunitContext
{
    private readonly StubUserPreferencesService _preferencesService = new();

    public UserPreferencesPageTests()
    {
        // Le panneau applique thème/densité via JS interop (stratumUI.*) : mode permissif.
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddLocalization();
        Services.AddSingleton<IUserPreferencesService>(_preferencesService);

        // Cache de langue du provider de culture : invalidé par le panneau au changement de langue.
        Services.AddMemoryCache();
        Services.AddSingleton<Liakont.Host.Localization.UserCultureCache>();
    }

    [Fact]
    public void Should_Render_Preferences_Panel_For_Anonymous_User()
    {
        UseUser(new ClaimsPrincipal(new ClaimsIdentity()));

        var cut = Render<PreferencesPage>();

        cut.FindAll("[data-testid='user-preferences-panel']").Should().ContainSingle();

        // Sans utilisateur résolu, aucune lecture en base ne doit être tentée.
        _preferencesService.GetCalls.Should().Be(0);
    }

    [Fact]
    public void Should_Reflect_Persisted_Preferences_For_Authenticated_User()
    {
        var userId = Guid.NewGuid();
        UseUser(AuthenticatedUser(userId));
        _preferencesService.Stored = UserPreferences.Default with
        {
            Theme = UserPreferences.ThemeDark,
            Density = UserPreferences.DensityCompact,
        };

        var cut = Render<PreferencesPage>();

        _preferencesService.GetCalls.Should().Be(1);
        _preferencesService.LastUserId.Should().Be(userId);
        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='pref-theme-dark']").GetAttribute("aria-pressed").Should().Be("true");
            cut.Find("[data-testid='pref-density-compact']").GetAttribute("aria-pressed").Should().Be("true");
        });
    }

    [Fact]
    public void Switching_The_Language_Invalidates_The_User_Culture_Cache()
    {
        // Culture effective déterministe : « en » actif → le bouton « fr » est cliquable.
        System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en");

        var userId = Guid.NewGuid();
        UseUser(AuthenticatedUser(userId));
        _preferencesService.Stored = UserPreferences.Default;

        var cut = Render<PreferencesPage>();
        var cache = Services.GetRequiredService<Liakont.Host.Localization.UserCultureCache>();
        cache.Set(userId, "en");

        cut.Find("[data-testid='pref-language-fr']").Click();

        // Sans l'invalidation, le provider de culture relirait « en » depuis le cache (TTL 5 min)
        // au rechargement : la nouvelle langue ne s'appliquerait pas immédiatement (bug-inbox langue).
        cut.WaitForAssertion(() =>
        {
            cache.TryGet(userId, out _).Should().BeFalse("le changement de langue doit invalider le cache du provider");
            _preferencesService.Stored!.Language.Should().Be("fr");
        });
    }

    private static ClaimsPrincipal AuthenticatedUser(Guid userId) =>
        new(new ClaimsIdentity(
            [new Claim("stratum_user_id", userId.ToString())],
            authenticationType: "test"));

    private void UseUser(ClaimsPrincipal principal) =>
        Services.AddSingleton<AuthenticationStateProvider>(new FakeAuthenticationStateProvider(principal));

    private sealed class FakeAuthenticationStateProvider(ClaimsPrincipal principal) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(principal));
    }

    private sealed class StubUserPreferencesService : IUserPreferencesService
    {
        public UserPreferences? Stored { get; set; }

        public int GetCalls { get; private set; }

        public Guid? LastUserId { get; private set; }

        public Task<UserPreferences?> GetAsync(Guid userId, CancellationToken ct = default)
        {
            GetCalls++;
            LastUserId = userId;
            return Task.FromResult(Stored);
        }

        public Task<UserPreferences> GetOrDefaultAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(Stored ?? UserPreferences.Default);

        public Task UpdateAsync(Guid userId, UserPreferences preferences, CancellationToken ct = default)
        {
            Stored = preferences;
            return Task.CompletedTask;
        }
    }
}
