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

        // Kind = catégorie produit lisible ; clé = slug lisible + suffixe d'empreinte injectif (16 hex).
        dir.Should().MatchRegex("^_ged/bordereau/2026/05/K-42-[0-9a-f]{16}/$");
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

        // Seul le NOM DE BASE d'un segment survit (anti path-traversal) : « ../etc » → kind « etc » (lisible),
        // « a/b/../c » → clé « c » assainie + suffixe d'empreinte injectif. Jamais de « .. » dans le chemin.
        dir.Should().MatchRegex("^_ged/etc/2026/01/c-[0-9a-f]{16}/$");
        dir.Should().NotContain("..");
    }

    [Fact]
    public void PackageDirectory_DistinctKeys_NeverCollide_EvenWhenSanitizationAliases()
    {
        // « K:42 » et « K?42 » s'assainissent tous deux en « K_42 » : sans encodage injectif ils tomberaient dans
        // le MÊME répertoire (conflit WORM permanent / fausse dédup). L'empreinte de la clé BRUTE les sépare.
        string a = GedArchivePackageLayout.PackageDirectory("kind", 2026, 5, "K:42");
        string b = GedArchivePackageLayout.PackageDirectory("kind", 2026, 5, "K?42");

        a.Should().NotBe(b);
        a.Should().MatchRegex("^_ged/kind/2026/05/K_42-[0-9a-f]{16}/$");
        b.Should().MatchRegex("^_ged/kind/2026/05/K_42-[0-9a-f]{16}/$");
    }

    [Fact]
    public void PackageDirectory_SameKey_IsDeterministic()
    {
        // L'encodage injectif reste DÉTERMINISTE : la même clé brute → le même répertoire (idempotence du rangement).
        string first = GedArchivePackageLayout.PackageDirectory("kind", 2026, 5, "K:42");
        string second = GedArchivePackageLayout.PackageDirectory("kind", 2026, 5, "K:42");

        second.Should().Be(first);
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
