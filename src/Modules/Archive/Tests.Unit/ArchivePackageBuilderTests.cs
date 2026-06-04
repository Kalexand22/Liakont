namespace Liakont.Modules.Archive.Tests.Unit;

using System.Linq;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Liakont.Modules.Archive.Application;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Archive.Domain;
using Xunit;

public sealed class ArchivePackageBuilderTests
{
    private static readonly ArchiveSealContext Seal = new("chain-hash-xyz", new System.DateTimeOffset(2026, 5, 12, 10, 0, 0, System.TimeSpan.Zero));

    [Fact]
    public void BuildPackageContent_WithAllPieces_ContainsThreeMandatoryAndTwoOptionalFiles()
    {
        ArchivePackageContent content = ArchivePackageBuilder.BuildPackageContent(ArchiveTestData.PackageRequest());

        content.ContentFiles.Select(f => f.Name).Should().BeEquivalentTo(
            "payload.json", "reponse-pa.json", "document-lisible.html", "facture-pa.pdf", "bordereau-source.pdf", "archive-metadata.json");
        content.AbsentPieces.Should().BeEmpty();
        content.PackageHash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void BuildPackageContent_WithoutOptionalPieces_RecordsAbsenceWithReason()
    {
        ArchivePackageContent content = ArchivePackageBuilder.BuildPackageContent(
            ArchiveTestData.PackageRequest(withPaInvoice: false, withSourceDocument: false));

        content.ContentFiles.Select(f => f.Name).Should().BeEquivalentTo(
            "payload.json", "reponse-pa.json", "document-lisible.html", "archive-metadata.json");
        content.AbsentPieces.Select(p => p.Piece).Should().BeEquivalentTo("facture-pa", "bordereau-source");
        content.AbsentPieces.Should().OnlyContain(p => p.Reason.Length > 0);
    }

    [Fact]
    public void BuildPackageManifest_SealsHashesAndListsFiles()
    {
        ArchivePackageRequest request = ArchiveTestData.PackageRequest(withPaInvoice: false, withSourceDocument: true);
        ArchivePackageContent content = ArchivePackageBuilder.BuildPackageContent(request);

        byte[] manifestBytes = ArchivePackageBuilder.BuildPackageManifest(request, content, Seal);
        using var document = JsonDocument.Parse(manifestBytes);
        JsonElement root = document.RootElement;

        root.GetProperty("entryKind").GetString().Should().Be("package");
        root.GetProperty("packageHash").GetString().Should().Be(content.PackageHash);
        root.GetProperty("chainHash").GetString().Should().Be("chain-hash-xyz");

        // payload + reponse-pa + document-lisible + bordereau-source + archive-metadata (facture-pa absente).
        root.GetProperty("files").GetArrayLength().Should().Be(5);
        root.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("name").GetString())
            .Should().Contain("archive-metadata.json");

        // absentPieces et mappingTrace sont désormais dans archive-metadata.json (haché), pas dans le manifest.
        root.TryGetProperty("absentPieces", out _).Should().BeFalse();
        root.TryGetProperty("mappingTrace", out _).Should().BeFalse();

        root.GetProperty("notice").GetString().Should().Contain("NF Z42-013");
    }

    [Fact]
    public void BuildAddendumContent_HashIsContentBased_NotNameBased()
    {
        ArchiveAddendumRequest request = ArchiveTestData.AddendumRequest(System.Guid.NewGuid());
        ArchivePackageContent content = ArchivePackageBuilder.BuildAddendumContent(request);

        content.ContentFiles.Should().ContainSingle();
        content.PackageHash.Should().Be(Sha256Hex.OfBytes(Encoding.UTF8.GetBytes("<ledger/>")));
    }
}
