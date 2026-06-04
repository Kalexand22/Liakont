namespace Liakont.Modules.Archive.Tests.Unit;

using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Archive.Domain;
using Liakont.Modules.Archive.Infrastructure;
using Liakont.Modules.Archive.Tests.Unit.Doubles;
using Xunit;

/// <summary>
/// Tests de l'ancrage RFC 3161 (TRK06) contre une TSA mockée (<see cref="TestTimestampAuthority"/>) qui
/// émet de VRAIS jetons signés : on exerce le chemin cryptographique natif réel (requête, ProcessResponse,
/// vérification de signature) sans TSA externe.
/// </summary>
public sealed class Rfc3161TimestampAnchorTests
{
    private static byte[] Digest(string seed = "chain-head") => SHA256.HashData(Encoding.UTF8.GetBytes(seed));

    [Fact]
    public async Task Anchor_ThenVerify_RoundTripsValid()
    {
        using var tsa = new TestTimestampAuthority();
        var anchor = new Rfc3161TimestampAnchor(FakeTsaClient.Backed(tsa));
        byte[] digest = Digest();

        TimestampAnchorResult result = await anchor.AnchorAsync(digest);

        result.IsAnchored.Should().BeTrue();
        result.Method.Should().Be(TimestampAnchorMethod.Rfc3161);
        result.Proof.Should().NotBeNull();
        result.AnchoredUtc.Should().Be(tsa.Timestamp);

        TimestampVerification verification = await anchor.VerifyAsync(result.Proof!, digest);
        verification.IsValid.Should().BeTrue();
        verification.AnchoredUtc.Should().Be(tsa.Timestamp);
    }

    [Fact]
    public async Task Verify_FailsForDifferentDigest()
    {
        using var tsa = new TestTimestampAuthority();
        var anchor = new Rfc3161TimestampAnchor(FakeTsaClient.Backed(tsa));
        TimestampAnchorResult result = await anchor.AnchorAsync(Digest());

        TimestampVerification verification = await anchor.VerifyAsync(result.Proof!, Digest("autre-tête"));

        verification.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Verify_FailsForGarbageProof()
    {
        var anchor = new Rfc3161TimestampAnchor(new FakeTsaClient(_ => []));

        TimestampVerification verification = await anchor.VerifyAsync(Encoding.UTF8.GetBytes("pas un jeton"), Digest());

        verification.IsValid.Should().BeFalse();
        verification.Detail.Should().Contain("illisible");
    }

    [Fact]
    public void Capabilities_AreOperationalImmediateOnline()
    {
        var anchor = new Rfc3161TimestampAnchor(new FakeTsaClient(_ => []));

        anchor.Capabilities.Method.Should().Be(TimestampAnchorMethod.Rfc3161);
        anchor.Capabilities.IsOperational.Should().BeTrue();
        anchor.Capabilities.ProducesImmediateProof.Should().BeTrue();
        anchor.Capabilities.RequiresOutboundInternet.Should().BeTrue();
    }
}
