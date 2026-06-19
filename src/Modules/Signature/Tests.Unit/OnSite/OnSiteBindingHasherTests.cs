namespace Liakont.Modules.Signature.Tests.Unit.OnSite;

using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Liakont.Modules.Signature.Infrastructure.OnSite;
using Xunit;

/// <summary>
/// Gardes de la primitive de binding (ADR-0030 §4 ; INV-ONSITE-6) : SHA-256 des OCTETS EXACTS, MÊME flux
/// client/plateforme, vérification <c>re-hash == hash signé</c> à temps constant et rejet d'une empreinte
/// portant sur d'autres octets ou malformée.
/// </summary>
public sealed class OnSiteBindingHasherTests
{
    [Fact]
    public void ComputeHex_IsDeterministicSha256_LowercaseHex()
    {
        // Vecteur SHA-256 connu de « abc » → le MÊME flux d'octets côté client ET plateforme produit ce hex.
        var hex = OnSiteBindingHasher.ComputeHex(Encoding.ASCII.GetBytes("abc"));

        hex.Should().Be("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }

    [Fact]
    public void Verify_ReturnsTrue_WhenSignedHashMatchesExactBytes()
    {
        var artifact = Encoding.UTF8.GetBytes("FACTURX-ARTIFACT-SCELLE-V1");
        var signedHex = Convert.ToHexString(SHA256.HashData(artifact)).ToLowerInvariant();

        OnSiteBindingHasher.Verify(artifact, signedHex).Should().BeTrue();
    }

    [Fact]
    public void Verify_ReturnsFalse_WhenClientHashedDifferentBytes()
    {
        var stored = Encoding.UTF8.GetBytes("FACTURX-ARTIFACT-SCELLE-V1");
        var tampered = Encoding.UTF8.GetBytes("FACTURX-ARTIFACT-ALTERE");
        var signedOverTampered = Convert.ToHexString(SHA256.HashData(tampered)).ToLowerInvariant();

        OnSiteBindingHasher.Verify(stored, signedOverTampered).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("zzzz-not-hex")]
    [InlineData("ab")] // hex valide mais 1 octet ≠ 32 octets SHA-256
    public void Verify_ReturnsFalse_WhenSignedHashIsMissingOrMalformed(string signedHex)
    {
        var artifact = Encoding.UTF8.GetBytes("FACTURX-ARTIFACT-SCELLE-V1");

        OnSiteBindingHasher.Verify(artifact, signedHex).Should().BeFalse();
    }
}
