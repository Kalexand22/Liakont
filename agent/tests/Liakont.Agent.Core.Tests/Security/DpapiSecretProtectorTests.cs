namespace Liakont.Agent.Core.Tests.Security;

using System;
using FluentAssertions;
using Liakont.Agent.Core.Security;
using Xunit;

/// <summary>
/// Round-trip DPAPI (CLAUDE.md n°10, F12 §2.4) : un secret chiffré puis déchiffré redonne la valeur
/// d'origine, et la forme protégée n'est jamais le clair. DPAPI portée machine fonctionne sous le
/// compte du test (Windows) — c'est précisément la portée que partagent service et CLI.
/// </summary>
public class DpapiSecretProtectorTests
{
    private readonly DpapiSecretProtector _protector = new DpapiSecretProtector();

    [Fact]
    public void Protect_then_Unprotect_returns_the_original_value()
    {
        const string secret = "pk_live_aBcD.1234-CLE-API-FICTIVE";

        string protectedValue = _protector.Protect(secret);
        string roundTripped = _protector.Unprotect(protectedValue);

        roundTripped.Should().Be(secret);
    }

    [Fact]
    public void Protected_value_is_not_the_plaintext_and_is_base64()
    {
        const string secret = "Driver={Pervasive ODBC};ServerName=FICTIF;DBQ=VENTES";

        string protectedValue = _protector.Protect(secret);

        protectedValue.Should().NotBe(secret);
        Action decode = () => Convert.FromBase64String(protectedValue);
        decode.Should().NotThrow("la forme protégée est encodée en base64");
    }

    [Fact]
    public void Protect_handles_unicode_secrets()
    {
        const string secret = "clé-é-à-ü-€-secret";

        string roundTripped = _protector.Unprotect(_protector.Protect(secret));

        roundTripped.Should().Be(secret);
    }
}
