namespace Liakont.Tests.E2E.Support;

using System;
using System.Text;
using Xunit;

/// <summary>
/// Vérifie <see cref="TotpGenerator"/> sur les vecteurs normatifs RFC 4226 (HOTP) et RFC 6238 (TOTP).
/// <para>
/// Test UNITAIRE pur (aucun Playwright, aucun conteneur) : volontairement SANS
/// <c>[Trait("Category","E2E")]</c> et SANS héritage de <c>KeycloakBaseE2ETest</c>, pour qu'il tourne
/// dans <c>run-tests</c> (filtre <c>Category!=E2E</c>) et prouve l'algorithme TOTP sans Keycloak.
/// C'est l'anti-faux-vert de la moitié « calcul du code 2FA » du chemin E2E (RLM01 DÉC-2) : la moitié
/// « accord avec Keycloak » est validée à la GATE humaine (login réel).
/// </para>
/// </summary>
public sealed class TotpGeneratorTests
{
    // Graine de référence RFC 4226/6238 (Appendice D / B) : ASCII "12345678901234567890" (20 octets, SHA1).
    private const string RfcSeed = "12345678901234567890";

    [Theory]
    [InlineData(0L, "755224")]
    [InlineData(1L, "287082")]
    [InlineData(2L, "359152")]
    [InlineData(3L, "969429")]
    [InlineData(4L, "338314")]
    [InlineData(5L, "254676")]
    [InlineData(6L, "287922")]
    [InlineData(7L, "162583")]
    [InlineData(8L, "399871")]
    [InlineData(9L, "520489")]
    public void GenerateForCounter_Matches_Rfc4226_Hotp_Vectors(long counter, string expected)
    {
        var key = Encoding.ASCII.GetBytes(RfcSeed);

        var code = TotpGenerator.GenerateForCounter(key, counter);

        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void Generate_Matches_Rfc6238_Totp_Sha1_Vectors(long unixSeconds, string expected)
    {
        var code = TotpGenerator.Generate(RfcSeed, DateTimeOffset.FromUnixTimeSeconds(unixSeconds));

        Assert.Equal(expected, code);
    }

    [Fact]
    public void Generate_IsStable_WithinSamePeriod_And_Changes_AcrossPeriods()
    {
        var secret = E2EUserOtpSecrets.ForUser("lecture");
        var t0 = DateTimeOffset.FromUnixTimeSeconds(1_700_000_010); // multiple de 30 → début d'une fenêtre
        var sameWindow = t0.AddSeconds(29);
        var nextWindow = t0.AddSeconds(30);

        Assert.Equal(TotpGenerator.Generate(secret, t0), TotpGenerator.Generate(secret, sameWindow));
        Assert.NotEqual(TotpGenerator.Generate(secret, t0), TotpGenerator.Generate(secret, nextWindow));
    }
}
