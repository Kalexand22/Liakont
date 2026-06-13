namespace Liakont.Tests.E2E.Support;

using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Xunit;

/// <summary>
/// Vérifie que chaque secret TOTP déclaré dans <see cref="E2EUserOtpSecrets"/> est identique au champ
/// <c>secretData.value</c> du credential <c>otp</c> du même utilisateur dans le fichier de realm E2E
/// (<c>keycloak-e2e-realm.json</c>).
/// <para>
/// Test UNITAIRE pur (aucun Playwright, aucun conteneur) : volontairement SANS
/// <c>[Trait("Category","E2E")]</c> et SANS héritage de <c>KeycloakBaseE2ETest</c>, pour qu'il tourne
/// dans <c>run-tests</c> (filtre <c>Category!=E2E</c>).
/// </para>
/// </summary>
public sealed class E2EUserOtpSecretsConsistencyTests
{
    [Fact]
    public void Every_OtpCredential_In_Realm_Matches_E2EUserOtpSecrets()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "keycloak-e2e-realm.json");
        File.Exists(fixturePath).Should().BeTrue(
            $"Fixture introuvable : {fixturePath} (CopyToOutputDirectory=PreserveNewest absent du csproj ?)");

        using var realm = JsonDocument.Parse(File.ReadAllText(fixturePath));

        foreach (var user in realm.RootElement.GetProperty("users").EnumerateArray())
        {
            var username = user.GetProperty("username").GetString()!;

            var otp = user.GetProperty("credentials").EnumerateArray()
                .FirstOrDefault(c => c.GetProperty("type").GetString() == "otp");

            if (otp.ValueKind == JsonValueKind.Undefined)
            {
                continue;
            }

            using var secretDoc = JsonDocument.Parse(otp.GetProperty("secretData").GetString()!);
            var expected = secretDoc.RootElement.GetProperty("value").GetString()!;

            var actual = E2EUserOtpSecrets.ForUser(username);

            actual.Should().Be(
                expected,
                $"le secret TOTP de '{username}' dans E2EUserOtpSecrets doit correspondre au champ secretData.value du realm E2E");
        }
    }
}
