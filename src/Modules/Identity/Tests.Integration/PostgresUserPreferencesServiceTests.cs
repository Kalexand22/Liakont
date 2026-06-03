namespace Stratum.Modules.Identity.Tests.Integration;

using FluentAssertions;
using Stratum.Modules.Identity.Application.Preferences;
using Stratum.Modules.Identity.Domain.Entities;
using Stratum.Modules.Identity.Infrastructure.Repositories;
using Stratum.Modules.Identity.Infrastructure.Services;
using Stratum.Modules.Identity.Tests.Integration.Fixtures;
using Xunit;

[Collection("Identity")]
public sealed class PostgresUserPreferencesServiceTests
{
    private readonly IdentityDatabaseFixture _fixture;

    private readonly PostgresUserPreferencesService _service;

    private readonly PostgresUserRepository _userRepo;

    public PostgresUserPreferencesServiceTests(IdentityDatabaseFixture fixture)
    {
        _fixture = fixture;
        var connFactory = fixture.CreateConnectionFactory();
        _service = new PostgresUserPreferencesService(connFactory);
        _userRepo = new PostgresUserRepository(connFactory);
    }

    [Fact]
    public async Task GetAsync_ShouldReturnNull_WhenNoRowExists()
    {
        var userId = await CreateUserAsync("pref_getnull");

        var result = await _service.GetAsync(userId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetOrDefaultAsync_ShouldReturnDefaults_WhenNoRowExists()
    {
        var userId = await CreateUserAsync("pref_getdefaults");

        var result = await _service.GetOrDefaultAsync(userId);

        result.Should().NotBeNull();
        result.Theme.Should().Be(UserPreferences.ThemeSystem);
        result.Language.Should().Be(UserPreferences.DefaultLanguage);
        result.Density.Should().Be(UserPreferences.DensityStandard);
        result.ExtensionsJson.Should().Be(UserPreferences.DefaultExtensionsJson);
    }

    [Fact]
    public async Task UpdateAsync_ShouldCreateRow_WhenCalledForTheFirstTime()
    {
        var userId = await CreateUserAsync("pref_insert");
        var preferences = new UserPreferences
        {
            Theme = UserPreferences.ThemeDark,
            Language = "en-US",
            Density = UserPreferences.DensityCompact,
            ExtensionsJson = "{\"sidebar\":\"collapsed\"}",
        };

        await _service.UpdateAsync(userId, preferences);
        var loaded = await _service.GetAsync(userId);

        loaded.Should().NotBeNull();
        loaded!.Theme.Should().Be(UserPreferences.ThemeDark);
        loaded.Language.Should().Be("en-US");
        loaded.Density.Should().Be(UserPreferences.DensityCompact);
        loaded.ExtensionsJson.Should().Contain("sidebar");
        loaded.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateAsync_ShouldUpsertExistingRow_WhenCalledTwice()
    {
        var userId = await CreateUserAsync("pref_upsert");
        var first = new UserPreferences
        {
            Theme = UserPreferences.ThemeLight,
            Language = "fr-FR",
            Density = UserPreferences.DensityStandard,
        };
        await _service.UpdateAsync(userId, first);

        var updated = new UserPreferences
        {
            Theme = UserPreferences.ThemeDark,
            Language = "fr-FR",
            Density = UserPreferences.DensityCompact,
            ExtensionsJson = "{\"density-override\":true}",
        };
        await _service.UpdateAsync(userId, updated);

        var loaded = await _service.GetAsync(userId);

        loaded.Should().NotBeNull();
        loaded!.Theme.Should().Be(UserPreferences.ThemeDark);
        loaded.Density.Should().Be(UserPreferences.DensityCompact);
        loaded.ExtensionsJson.Should().Contain("density-override");
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrow_WhenThemeIsInvalid()
    {
        var userId = await CreateUserAsync("pref_badtheme");
        var invalid = new UserPreferences { Theme = "neon", Language = "fr-FR", Density = UserPreferences.DensityStandard };

        var act = async () => await _service.UpdateAsync(userId, invalid);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrow_WhenDensityIsInvalid()
    {
        var userId = await CreateUserAsync("pref_baddensity");
        var invalid = new UserPreferences { Theme = UserPreferences.ThemeSystem, Language = "fr-FR", Density = "xl" };

        var act = async () => await _service.UpdateAsync(userId, invalid);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrow_WhenLanguageIsEmpty()
    {
        var userId = await CreateUserAsync("pref_emptylang");
        var invalid = new UserPreferences { Theme = UserPreferences.ThemeSystem, Language = "  ", Density = UserPreferences.DensityStandard };

        var act = async () => await _service.UpdateAsync(userId, invalid);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrow_WhenLanguageIsUnknownCulture()
    {
        var userId = await CreateUserAsync("pref_badlang");
        var invalid = new UserPreferences { Theme = UserPreferences.ThemeSystem, Language = "not-a-culture", Density = UserPreferences.DensityStandard };

        var act = async () => await _service.UpdateAsync(userId, invalid);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrow_WhenExtensionsJsonIsMalformed()
    {
        var userId = await CreateUserAsync("pref_badjson");
        var invalid = new UserPreferences
        {
            Theme = UserPreferences.ThemeSystem,
            Language = UserPreferences.DefaultLanguage,
            Density = UserPreferences.DensityStandard,
            ExtensionsJson = "{broken",
        };

        var act = async () => await _service.UpdateAsync(userId, invalid);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrow_WhenExtensionsJsonIsNotAnObject()
    {
        var userId = await CreateUserAsync("pref_jsonarray");
        var invalid = new UserPreferences
        {
            Theme = UserPreferences.ThemeSystem,
            Language = UserPreferences.DefaultLanguage,
            Density = UserPreferences.DensityStandard,
            ExtensionsJson = "[1,2,3]",
        };

        var act = async () => await _service.UpdateAsync(userId, invalid);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrow_WhenExtensionsJsonExceedsSizeLimit()
    {
        var userId = await CreateUserAsync("pref_bigjson");
        var oversize = "{\"k\":\"" + new string('x', UserPreferences.MaxExtensionsJsonBytes) + "\"}";
        var invalid = new UserPreferences
        {
            Theme = UserPreferences.ThemeSystem,
            Language = UserPreferences.DefaultLanguage,
            Density = UserPreferences.DensityStandard,
            ExtensionsJson = oversize,
        };

        var act = async () => await _service.UpdateAsync(userId, invalid);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetOrDefaultAsync_ShouldReturnStoredRow_WhenItExists()
    {
        var userId = await CreateUserAsync("pref_storedwins");
        await _service.UpdateAsync(userId, new UserPreferences
        {
            Theme = UserPreferences.ThemeLight,
            Language = "de-DE",
            Density = UserPreferences.DensityCompact,
        });

        var result = await _service.GetOrDefaultAsync(userId);

        result.Theme.Should().Be(UserPreferences.ThemeLight);
        result.Language.Should().Be("de-DE");
        result.Density.Should().Be(UserPreferences.DensityCompact);
    }

    private async Task<Guid> CreateUserAsync(string suffix)
    {
        var user = User.CreateFromOidc(
            $"kc-{suffix}",
            suffix,
            $"{suffix}@example.com",
            $"Pref {suffix}");
        await _userRepo.Insert(user);
        return user.Id;
    }
}
