namespace Liakont.Modules.Archive.Tests.Unit;

using System;
using System.Collections.Generic;
using FluentAssertions;
using Liakont.Modules.Archive.Domain;
using Xunit;

public sealed class PackageHasherTests
{
    [Fact]
    public void Compute_IsIndependentOfOrder()
    {
        var ordered = new List<ArchiveFileFingerprint>
        {
            new("payload.json", "h1"),
            new("reponse-pa.json", "h2"),
        };
        var reversed = new List<ArchiveFileFingerprint>
        {
            new("reponse-pa.json", "h2"),
            new("payload.json", "h1"),
        };

        PackageHasher.Compute(ordered).Should().Be(PackageHasher.Compute(reversed));
    }

    [Fact]
    public void Compute_ChangesWhenAFileHashChanges()
    {
        var baseline = new List<ArchiveFileFingerprint> { new("payload.json", "h1") };
        var altered = new List<ArchiveFileFingerprint> { new("payload.json", "h1-altered") };

        PackageHasher.Compute(altered).Should().NotBe(PackageHasher.Compute(baseline));
    }

    [Fact]
    public void Compute_ChangesWhenAFileNameChanges()
    {
        var baseline = new List<ArchiveFileFingerprint> { new("payload.json", "h1") };
        var renamed = new List<ArchiveFileFingerprint> { new("payload.txt", "h1") };

        PackageHasher.Compute(renamed).Should().NotBe(PackageHasher.Compute(baseline));
    }

    [Fact]
    public void Compute_Empty_Throws()
    {
        Action act = () => PackageHasher.Compute([]);
        act.Should().Throw<ArgumentException>();
    }
}
