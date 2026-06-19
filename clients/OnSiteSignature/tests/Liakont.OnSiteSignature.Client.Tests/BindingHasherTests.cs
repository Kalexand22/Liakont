namespace Liakont.OnSiteSignature.Client.Tests;

using System.Text;
using FluentAssertions;
using Xunit;

/// <summary>
/// Garde du binding hash côté client (ADR-0030 §4 ; INV-ONSITE-6) : SHA-256 hex minuscule déterministe sur
/// les octets exacts. Le vecteur connu « abc » est IDENTIQUE à celui produit côté plateforme
/// (<c>OnSiteBindingHasher.ComputeHex</c>) — même flux d'octets, même algorithme, même encodage.
/// </summary>
public sealed class BindingHasherTests
{
    [Fact]
    public void ComputeHex_KnownVector_MatchesPlatform()
    {
        var hex = BindingHasher.ComputeHex(Encoding.ASCII.GetBytes("abc"));

        hex.Should().Be("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }

    [Fact]
    public void ComputeHex_DifferentBytes_DifferentHash()
    {
        var a = BindingHasher.ComputeHex(Encoding.UTF8.GetBytes("FACTURX-A"));
        var b = BindingHasher.ComputeHex(Encoding.UTF8.GetBytes("FACTURX-B"));

        a.Should().NotBe(b);
    }
}
