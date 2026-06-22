namespace Liakont.Host.Tests.Unit.Security;

using System;
using System.Globalization;
using FluentAssertions;
using Liakont.Host.Security;
using Liakont.Host.Security.Keycloak;
using Xunit;

/// <summary>
/// Vérifie que <see cref="KeycloakIdentityProviderAuthenticator.ValidateConfiguration"/> rejette
/// une <see cref="KeycloakSettings.SensitivePermissionRevocationWindow"/> invalide (≤ zéro) et accepte
/// une valeur positive — garantissant le fail-closed au démarrage de la mitigation RDF10/ADR-0017 §Négatif.
/// </summary>
public sealed class KeycloakSettingsValidationTests
{
    private const string ValidAuthority = "https://kc.example/realms/liakont";

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-00:00:01")]
    public void ValidateConfiguration_Throws_When_SensitivePermissionRevocationWindow_Is_Not_Positive(string windowStr)
    {
        var window = TimeSpan.Parse(windowStr, CultureInfo.InvariantCulture);
        var settings = new KeycloakSettings
        {
            Authority = ValidAuthority,
            SensitivePermissionRevocationWindow = window,
        };

        var act = () => new KeycloakIdentityProviderAuthenticator(settings).ValidateConfiguration();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SensitivePermissionRevocationWindow*");
    }

    [Fact]
    public void ValidateConfiguration_Does_Not_Throw_When_Configuration_Is_Valid()
    {
        var settings = new KeycloakSettings
        {
            Authority = ValidAuthority,
            SensitivePermissionRevocationWindow = TimeSpan.FromMinutes(30),
        };

        var act = () => new KeycloakIdentityProviderAuthenticator(settings).ValidateConfiguration();

        act.Should().NotThrow();
    }
}
