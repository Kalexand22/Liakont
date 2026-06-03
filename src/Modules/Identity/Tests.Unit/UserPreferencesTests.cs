namespace Stratum.Modules.Identity.Tests.Unit;

using FluentAssertions;
using Stratum.Modules.Identity.Application.Preferences;
using Xunit;

public sealed class UserPreferencesTests
{
    [Fact]
    public void Default_ShouldUseSystemThemeFrenchFranceAndStandardDensity()
    {
        var defaults = UserPreferences.Default;

        defaults.Theme.Should().Be(UserPreferences.ThemeSystem);
        defaults.Language.Should().Be(UserPreferences.DefaultLanguage);
        defaults.Density.Should().Be(UserPreferences.DensityStandard);
        defaults.ExtensionsJson.Should().Be(UserPreferences.DefaultExtensionsJson);
        defaults.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void Default_ShouldReturnSingletonInstance()
    {
        var a = UserPreferences.Default;
        var b = UserPreferences.Default;

        a.Should().BeSameAs(b);
    }

    [Fact]
    public void WithExpression_ShouldOverrideTheme_WhenCalledOnDefault()
    {
        var dark = UserPreferences.Default with { Theme = UserPreferences.ThemeDark };

        dark.Theme.Should().Be(UserPreferences.ThemeDark);
        dark.Language.Should().Be(UserPreferences.DefaultLanguage);
        UserPreferences.Default.Theme.Should().Be(UserPreferences.ThemeSystem,
            "the default singleton must remain immutable");
    }

    [Fact]
    public void Equality_ShouldBeStructural_WhenFieldsMatch()
    {
        var a = new UserPreferences { Theme = "dark", Language = "en-US", Density = "compact" };
        var b = new UserPreferences { Theme = "dark", Language = "en-US", Density = "compact" };

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }
}
