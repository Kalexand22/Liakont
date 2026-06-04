namespace Liakont.Modules.Archive.Tests.Unit;

using System.Text;
using FluentAssertions;
using Liakont.Modules.Archive.Domain;
using Xunit;

public sealed class Sha256HexTests
{
    [Fact]
    public void OfString_EmptyString_MatchesKnownVector()
    {
        // Vecteur connu SHA-256("") — verrouille la convention (hex minuscule, 64 caractères).
        Sha256Hex.OfString(string.Empty)
            .Should().Be("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    }

    [Fact]
    public void OfString_AbcVector_IsCorrect()
    {
        Sha256Hex.OfString("abc")
            .Should().Be("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }

    [Fact]
    public void OfBytes_And_OfString_AreConsistent()
    {
        Sha256Hex.OfBytes(Encoding.UTF8.GetBytes("liakont"))
            .Should().Be(Sha256Hex.OfString("liakont"));
    }

    [Fact]
    public void OfString_ProducesLowercaseHexOf64Chars()
    {
        string hash = Sha256Hex.OfString("payload");
        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }
}
