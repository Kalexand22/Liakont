namespace Liakont.Host.Tests.Unit.Pages;

using System.Text.Json.Nodes;
using FluentAssertions;
using Liakont.Host.Components.Settings;
using Stratum.Modules.Identity.Application.Preferences;
using Xunit;

/// <summary>
/// Tests unitaires du helper de préférences (RBF08) : taille de page de grille portée en base
/// via le point d'extension <c>ExtensionsJson</c> du modèle vendored <see cref="UserPreferences"/>.
/// </summary>
public sealed class UserPreferencesSupportTests
{
    [Fact]
    public void GetGridPageSize_ReturnsDefault_WhenExtensionsJsonIsEmptyObject()
    {
        var prefs = UserPreferences.Default;

        UserPreferencesSupport.GetGridPageSize(prefs)
            .Should().Be(UserPreferencesSupport.DefaultGridPageSize);
        UserPreferencesSupport.HasExplicitGridPageSize(prefs).Should().BeFalse();
    }

    [Theory]
    [InlineData(10)]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(100)]
    public void WithGridPageSize_RoundTripsAllowedValues(int size)
    {
        var prefs = UserPreferencesSupport.WithGridPageSize(UserPreferences.Default, size);

        UserPreferencesSupport.HasExplicitGridPageSize(prefs).Should().BeTrue();
        UserPreferencesSupport.GetGridPageSize(prefs).Should().Be(size);
    }

    [Fact]
    public void WithGridPageSize_PreservesOtherExtensionKeys()
    {
        var prefs = UserPreferences.Default with { ExtensionsJson = "{\"sidebar\":\"collapsed\"}" };

        var updated = UserPreferencesSupport.WithGridPageSize(prefs, 50);

        var obj = JsonNode.Parse(updated.ExtensionsJson)!.AsObject();
        obj["sidebar"]!.GetValue<string>().Should().Be("collapsed");
        obj[UserPreferencesSupport.GridPageSizeKey]!.GetValue<int>().Should().Be(50);
    }

    [Fact]
    public void WithGridPageSize_Rejects_NotAllowedValue()
    {
        var act = () => UserPreferencesSupport.WithGridPageSize(UserPreferences.Default, 33);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GetGridPageSize_IgnoresNotAllowedStoredValue()
    {
        var prefs = UserPreferences.Default with { ExtensionsJson = "{\"gridPageSize\":33}" };

        UserPreferencesSupport.GetGridPageSize(prefs)
            .Should().Be(UserPreferencesSupport.DefaultGridPageSize);
        UserPreferencesSupport.HasExplicitGridPageSize(prefs).Should().BeFalse();
    }

    [Fact]
    public void GetGridPageSize_IgnoresNonNumericStoredValue()
    {
        var prefs = UserPreferences.Default with { ExtensionsJson = "{\"gridPageSize\":\"50\"}" };

        UserPreferencesSupport.GetGridPageSize(prefs)
            .Should().Be(UserPreferencesSupport.DefaultGridPageSize);
        UserPreferencesSupport.HasExplicitGridPageSize(prefs).Should().BeFalse();
    }

    [Fact]
    public void GetGridPageSize_FallsBackToDefault_WhenExtensionsJsonIsMalformed()
    {
        var prefs = UserPreferences.Default with { ExtensionsJson = "{broken" };

        UserPreferencesSupport.GetGridPageSize(prefs)
            .Should().Be(UserPreferencesSupport.DefaultGridPageSize);
        UserPreferencesSupport.HasExplicitGridPageSize(prefs).Should().BeFalse();
    }

    [Fact]
    public void WithGridPageSize_RecoversFromMalformedExtensionsJson()
    {
        var prefs = UserPreferences.Default with { ExtensionsJson = "not json" };

        var updated = UserPreferencesSupport.WithGridPageSize(prefs, 25);

        UserPreferencesSupport.GetGridPageSize(updated).Should().Be(25);
    }

    [Fact]
    public void Helpers_Tolerate_A_Null_ExtensionsJson_Without_Throwing()
    {
        // Robustesse (review P2 round 2) : un ExtensionsJson NULL (colonne socle non coalescée) lèverait
        // ArgumentNullException dans JsonNode.Parse — non rattrapée par catch (JsonException). Les trois helpers
        // doivent retomber sur le défaut, et WithGridPageSize repartir d'un objet vide, sans propager.
        var prefs = UserPreferences.Default with { ExtensionsJson = null! };

        UserPreferencesSupport.GetGridPageSize(prefs).Should().Be(UserPreferencesSupport.DefaultGridPageSize);
        UserPreferencesSupport.HasExplicitGridPageSize(prefs).Should().BeFalse();

        var updated = UserPreferencesSupport.WithGridPageSize(prefs, 50);
        UserPreferencesSupport.GetGridPageSize(updated).Should().Be(50);
    }
}
