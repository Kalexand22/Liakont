namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components.Settings;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Modules.Identity.Application.Preferences;
using Xunit;

/// <summary>
/// Tests bUnit de l'hydrateur de préférences (RBF08) : applique thème / densité / taille de page
/// depuis la BASE vers la couche client (stratumUI) dès le rendu du shell, pour que la préférence
/// persistée s'applique quel que soit le navigateur.
/// </summary>
public sealed class UserPreferencesHydratorTests : BunitContext
{
    private readonly StubUserPreferencesService _preferencesService = new();

    public UserPreferencesHydratorTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddSingleton<IUserPreferencesService>(_preferencesService);
    }

    [Fact]
    public void Applies_persisted_theme_density_and_page_size_from_database()
    {
        var userId = Guid.NewGuid();
        UseUser(AuthenticatedUser(userId));
        _preferencesService.Stored = UserPreferences.Default with
        {
            Theme = UserPreferences.ThemeDark,
            Density = UserPreferences.DensityCompact,
            ExtensionsJson = "{\"gridPageSize\":50}",
        };

        Render<UserPreferencesHydrator>();

        _preferencesService.GetCalls.Should().Be(1);
        _preferencesService.LastUserId.Should().Be(userId);

        JSInterop.VerifyInvoke("stratumUI.setTheme").Arguments[0].Should().Be("dark");
        JSInterop.VerifyInvoke("stratumUI.setDensity").Arguments[0].Should().Be(UserPreferences.DensityCompact);
        JSInterop.VerifyInvoke("stratumUI.setGridPageSize").Arguments[0].Should().Be(50);
    }

    [Fact]
    public void Does_not_force_any_value_when_no_database_row_exists()
    {
        UseUser(AuthenticatedUser(Guid.NewGuid()));
        _preferencesService.Stored = null;

        Render<UserPreferencesHydrator>();

        _preferencesService.GetCalls.Should().Be(1);
        Invocations("stratumUI.setTheme").Should().BeEmpty();
        Invocations("stratumUI.setDensity").Should().BeEmpty();
        Invocations("stratumUI.setGridPageSize").Should().BeEmpty();
    }

    [Fact]
    public void Leaves_js_layer_in_charge_for_implicit_values()
    {
        UseUser(AuthenticatedUser(Guid.NewGuid()));

        // Theme "system" + no density + no page size key = no explicit choice in DB.
        _preferencesService.Stored = UserPreferences.Default;

        Render<UserPreferencesHydrator>();

        Invocations("stratumUI.setTheme").Should().BeEmpty("theme 'system' is not an explicit choice");
        Invocations("stratumUI.setGridPageSize").Should().BeEmpty("no page size persisted in the database");

        // Density "standard" is an explicit, valid value in the model default — it IS applied.
        JSInterop.VerifyInvoke("stratumUI.setDensity").Arguments[0].Should().Be(UserPreferences.DensityStandard);
    }

    [Fact]
    public void Does_not_read_the_database_for_an_anonymous_user()
    {
        UseUser(new ClaimsPrincipal(new ClaimsIdentity()));

        Render<UserPreferencesHydrator>();

        _preferencesService.GetCalls.Should().Be(0);
        Invocations("stratumUI.setTheme").Should().BeEmpty();
        Invocations("stratumUI.setDensity").Should().BeEmpty();
        Invocations("stratumUI.setGridPageSize").Should().BeEmpty();
    }

    private static ClaimsPrincipal AuthenticatedUser(Guid userId) =>
        new(new ClaimsIdentity(
            [new Claim("stratum_user_id", userId.ToString())],
            authenticationType: "test"));

    private System.Collections.Generic.List<JSRuntimeInvocation> Invocations(string identifier) =>
        JSInterop.Invocations.Where(i => i.Identifier == identifier).ToList();

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
