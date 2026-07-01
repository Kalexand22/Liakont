namespace Liakont.Modules.Archive.Tests.Unit;

using System;
using FluentAssertions;
using Liakont.Modules.Archive.Domain;
using Xunit;

/// <summary>Arborescence d'un paquet GED (F19 §5.1) : préfixe _ged/, assainissement, séparation de la chaîne fiscale.</summary>
public sealed class GedArchivePackageLayoutTests
{
    [Fact]
    public void PackageDirectory_BuildsGedPrefixedTree()
    {
        string dir = GedArchivePackageLayout.PackageDirectory("bordereau", 2026, 5, "K-42");

        dir.Should().Be("_ged/bordereau/2026/05/K-42/");
    }

    [Fact]
    public void PackageDirectory_IsDisjointFromFiscalChainTree()
    {
        // La chaîne fiscale vit sous {année}/{mois}/… — un paquet GED est structurellement ailleurs (_ged/…).
        string ged = GedArchivePackageLayout.PackageDirectory("kind", 2026, 5, "K-1");
        string fiscal = ArchivePackageLayout.PackageDirectory(2026, 5, "F-2026-001");

        ged.Should().StartWith(GedArchivePackageLayout.GedRootSegment + "/");
        fiscal.Should().NotStartWith(GedArchivePackageLayout.GedRootSegment + "/");
    }

    [Fact]
    public void PackageDirectory_SanitizesKindAndKey_AntiPathTraversal()
    {
        string dir = GedArchivePackageLayout.PackageDirectory("../etc", 2026, 1, "a/b/../c");

        // Seul le NOM DE BASE d'un segment survit (anti path-traversal) : « ../etc » → « etc », « a/b/../c » → « c ».
        dir.Should().Be("_ged/etc/2026/01/c/");
        dir.Should().NotContain("..");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void PackageDirectory_InvalidMonth_Throws(int month)
    {
        Action act = () => GedArchivePackageLayout.PackageDirectory("kind", 2026, month, "key");

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void PackageDirectory_EmptyKind_Throws()
    {
        Action act = () => GedArchivePackageLayout.PackageDirectory(string.Empty, 2026, 5, "key");

        act.Should().Throw<ArgumentException>();
    }
}
