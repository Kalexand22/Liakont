namespace Liakont.Host.Tests.Unit.Localization;

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Modules.Identity.Application.Preferences;
using Xunit;

/// <summary>
/// Le provider de culture lit la préférence Language PERSISTÉE (base = source de vérité, décision
/// opérateur 2026-06-10) pour les requêtes authentifiées, avec cache court ; il ne casse jamais la
/// requête (repli cookie/défaut sur toute anomalie).
/// </summary>
public sealed class PersistedLanguageRequestCultureProviderTests
{
    [Fact]
    public void The_Default_Culture_Is_French()
    {
        // Produit de conformité fiscale français : « fr » par défaut, jamais « en ».
        SupportedCultures.DefaultCulture.Should().Be("fr");
    }

    [Fact]
    public async Task An_Anonymous_Request_Falls_Through_To_The_Next_Provider()
    {
        var (provider, context, _) = Build(stored: null, principal: new ClaimsPrincipal(new ClaimsIdentity()));

        var result = await provider.DetermineProviderCultureResult(context);

        result.Should().BeNull("le cookie reste le repli des requêtes anonymes (ex. /login)");
    }

    [Fact]
    public async Task The_Persisted_Language_Of_An_Authenticated_User_Drives_The_Culture()
    {
        var (provider, context, _) = Build(UserPreferences.Default with { Language = "fr" });

        var result = await provider.DetermineProviderCultureResult(context);

        result.Should().NotBeNull();
        result!.Cultures.Should().ContainSingle().Which.ToString().Should().Be("fr");
        result.UICultures.Should().ContainSingle().Which.ToString().Should().Be("fr");
    }

    [Fact]
    public async Task A_Legacy_Specific_Culture_Value_Falls_Back_To_The_Supported_Neutral_Culture()
    {
        var (provider, context, _) = Build(UserPreferences.Default with { Language = "fr-FR" });

        var result = await provider.DetermineProviderCultureResult(context);

        result.Should().NotBeNull();
        result!.Cultures.Should().ContainSingle().Which.ToString().Should().Be("fr");
    }

    [Fact]
    public async Task An_Unsupported_Persisted_Language_Is_Ignored()
    {
        var (provider, context, _) = Build(UserPreferences.Default with { Language = "de" });

        var result = await provider.DetermineProviderCultureResult(context);

        result.Should().BeNull();
    }

    [Fact]
    public async Task The_Preference_Read_Is_Cached_Even_When_The_User_Has_No_Preference_Row()
    {
        var (provider, context, service) = Build(stored: null);

        (await provider.DetermineProviderCultureResult(context)).Should().BeNull();
        (await provider.DetermineProviderCultureResult(context)).Should().BeNull();

        service.GetCalls.Should().Be(1, "l'absence de préférence est aussi mémorisée (pas de lecture base par requête)");
    }

    [Fact]
    public async Task Invalidating_The_Cache_Forces_A_Fresh_Read()
    {
        var userId = Guid.NewGuid();
        var (provider, context, service) = Build(UserPreferences.Default with { Language = "en" }, userId: userId);

        (await provider.DetermineProviderCultureResult(context))!.Cultures[0].ToString().Should().Be("en");

        service.Stored = UserPreferences.Default with { Language = "fr" };
        context.RequestServices.GetRequiredService<UserCultureCache>().Invalidate(userId);

        (await provider.DetermineProviderCultureResult(context))!.Cultures[0].ToString().Should().Be("fr");
        service.GetCalls.Should().Be(2);
    }

    [Fact]
    public async Task A_Database_Failure_Never_Breaks_The_Request()
    {
        var (provider, context, _) = Build(stored: null, throws: true);

        var result = await provider.DetermineProviderCultureResult(context);

        result.Should().BeNull("la culture retombe sur cookie/défaut, la requête continue");
    }

    private static (PersistedLanguageRequestCultureProvider Provider, DefaultHttpContext Context, StubUserPreferencesService Service) Build(
        UserPreferences? stored,
        ClaimsPrincipal? principal = null,
        Guid? userId = null,
        bool throws = false)
    {
        var id = userId ?? Guid.NewGuid();
        var service = new StubUserPreferencesService { Stored = stored, Throws = throws };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddSingleton<UserCultureCache>();
        services.AddSingleton<IUserPreferencesService>(service);

        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            User = principal ?? new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("stratum_user_id", id.ToString())],
                authenticationType: "test")),
        };

        return (new PersistedLanguageRequestCultureProvider(), context, service);
    }

    private sealed class StubUserPreferencesService : IUserPreferencesService
    {
        public UserPreferences? Stored { get; set; }

        public bool Throws { get; set; }

        public int GetCalls { get; private set; }

        public Task<UserPreferences?> GetAsync(Guid userId, CancellationToken ct = default)
        {
            GetCalls++;
            return Throws
                ? throw new InvalidOperationException("Échec simulé de lecture des préférences.")
                : Task.FromResult(Stored);
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
