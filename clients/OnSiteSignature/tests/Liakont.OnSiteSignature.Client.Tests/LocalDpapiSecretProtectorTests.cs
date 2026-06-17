namespace Liakont.OnSiteSignature.Client.Tests;

using FluentAssertions;
using Xunit;

/// <summary>
/// Garde de la protection DPAPI LOCALE du client (ADR-0030 §7 ; INV-ONSITE-9) : un secret protégé puis
/// déprotégé revient à l'identique, et la forme protégée n'est jamais le clair (chiffré au repos).
/// </summary>
public sealed class LocalDpapiSecretProtectorTests
{
    [Fact]
    public void Protect_ThenUnprotect_RoundTrips()
    {
        var protector = new LocalDpapiSecretProtector();
        const string Secret = "cle-api-plateforme-poste-criee";

        var protectedValue = protector.Protect(Secret);

        protectedValue.Should().NotBe(Secret, "le secret est chiffré au repos (jamais en clair).");
        protector.Unprotect(protectedValue).Should().Be(Secret);
    }
}
