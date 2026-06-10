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
