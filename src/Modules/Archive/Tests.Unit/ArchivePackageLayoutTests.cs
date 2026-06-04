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
    public void AddendumManifestFileName_IsSequenced()
    {
        ArchivePackageLayout.AddendumManifestFileName(1).Should().Be("manifest-addendum-001.json");
        ArchivePackageLayout.AddendumManifestFileName(42).Should().Be("manifest-addendum-042.json");
    }

    [Fact]
    public void Combine_AppendsSanitizedFileName()
    {
        ArchivePackageLayout.Combine("2026/05/F-1/", "manifest.json")
            .Should().Be("2026/05/F-1/manifest.json");
    }
}
