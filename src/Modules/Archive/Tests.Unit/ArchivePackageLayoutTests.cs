namespace Liakont.Modules.Archive.Tests.Unit;

using System;
using FluentAssertions;
using Liakont.Modules.Archive.Domain;
using Xunit;

public sealed class ArchivePackageLayoutTests
{
    [Fact]
    public void PackageDirectory_FollowsYearMonthNumberConvention()
    {
        ArchivePackageLayout.PackageDirectory(2026, 5, "F-2026-001")
            .Should().Be("2026/05/F-2026-001/");
    }

    [Fact]
    public void PackageDirectory_SanitizesDocumentNumber()
    {
        ArchivePackageLayout.PackageDirectory(2026, 5, "F/2026 001")
            .Should().Be("2026/05/2026_001/");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void PackageDirectory_InvalidMonth_Throws(int month)
    {
        Action act = () => ArchivePackageLayout.PackageDirectory(2026, month, "F-1");
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SanitizeSegment_StripsPathTraversal()
    {
        ArchivePackageLayout.SanitizeSegment("../../etc/passwd").Should().Be("passwd");
        ArchivePackageLayout.SanitizeSegment("a/b/c.json").Should().Be("c.json");
    }

    [Fact]
    public void SanitizeSegment_EmptyOrDotted_Throws()
    {
        Action empty = () => ArchivePackageLayout.SanitizeSegment("///");
        empty.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddendumManifestFileName_UsesContentHashPrefix()
    {
        ArchivePackageLayout.AddendumManifestFileName("abc123").Should().Be("manifest-addendum-abc123.json");
        ArchivePackageLayout.AddendumManifestFileName("3122b57597b53441").Should().Be("manifest-addendum-3122b57597b53441.json");
    }

    [Fact]
    public void AddendumDataFileName_PrefixesWithHashAndFileName()
    {
        ArchivePackageLayout.AddendumDataFileName("abc123", "tax-report.xml")
            .Should().Be("addendum-abc123-tax-report.xml");
    }

    [Fact]
    public void Combine_AppendsSanitizedFileName()
    {
        ArchivePackageLayout.Combine("2026/05/F-1/", "manifest.json")
            .Should().Be("2026/05/F-1/manifest.json");
    }
}
