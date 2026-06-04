namespace Liakont.Modules.Archive.Tests.Unit;

using System;
using FluentAssertions;
using Liakont.Modules.Archive.Domain;
using Xunit;

public sealed class HashChainTests
{
    [Fact]
    public void Next_Genesis_UsesEmptyPrevious()
    {
        string entryHash = Sha256Hex.OfString("package-1");
        HashChain.Next(null, entryHash).Should().Be(Sha256Hex.OfString(entryHash));
        HashChain.Next(string.Empty, entryHash).Should().Be(Sha256Hex.OfString(entryHash));
    }

    [Fact]
    public void Next_ChainsPreviousAndEntry()
    {
        HashChain.Next("aaa", "bbb").Should().Be(Sha256Hex.OfString("aaabbb"));
    }

    [Fact]
    public void Next_IsOrderSensitive()
    {
        HashChain.Next("aaa", "bbb").Should().NotBe(HashChain.Next("bbb", "aaa"));
    }

    [Fact]
    public void Next_DifferentEntry_BreaksChainFromThatPoint()
    {
        string c1 = HashChain.Next(null, Sha256Hex.OfString("p1"));
        string honest = HashChain.Next(c1, Sha256Hex.OfString("p2"));
        string tampered = HashChain.Next(c1, Sha256Hex.OfString("p2-altered"));
        tampered.Should().NotBe(honest);
    }

    [Fact]
    public void Next_EmptyEntryHash_Throws()
    {
        Action act = () => HashChain.Next("prev", string.Empty);
        act.Should().Throw<ArgumentException>();
    }
}
