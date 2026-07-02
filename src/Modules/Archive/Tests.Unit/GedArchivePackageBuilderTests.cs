namespace Liakont.Modules.Archive.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Liakont.Modules.Archive.Application;
using Liakont.Modules.Archive.Contracts;
using Xunit;

/// <summary>Composition d'un paquet GED (F19 §5.1) : empreinte déterministe, axes d'index, RL-19 (confidentiel → valeur nulle).</summary>
public sealed class GedArchivePackageBuilderTests
{
    private static readonly DateTimeOffset ArchivedUtc = new(2026, 5, 12, 10, 0, 0, TimeSpan.Zero);

    private static GedArchivePackageRequest Request(
        IReadOnlyList<ArchiveAttachment>? contents = null,
        string? readableHtml = null,
        IReadOnlyList<ArchiveIndexAxis>? indexAxes = null) => new(
        ArchiveKind: "bordereau",
        ArchiveKey: "K-42",
        FiledOn: new DateOnly(2026, 5, 12),
        Contents: contents ?? [new ArchiveAttachment("piece.pdf", "application/pdf", Encoding.UTF8.GetBytes("%PDF-x"))],
        ReadableHtml: readableHtml,
        IndexAxes: indexAxes ?? []);

    [Fact]
    public void BuildPackageContent_HashIsDeterministic()
    {
        ArchivePackageContent a = GedArchivePackageBuilder.BuildPackageContent(Request());
        ArchivePackageContent b = GedArchivePackageBuilder.BuildPackageContent(Request());

        a.PackageHash.Should().MatchRegex("^[0-9a-f]{64}$");
        b.PackageHash.Should().Be(a.PackageHash);
    }

    [Fact]
    public void BuildPackageContent_IncludesReadableHtml_WhenProvided()
    {
        ArchivePackageContent withHtml = GedArchivePackageBuilder.BuildPackageContent(Request(readableHtml: "<p>aperçu</p>"));

        withHtml.ContentFiles.Select(f => f.Name).Should().Contain("document-lisible.html");
    }

    [Fact]
    public void BuildPackageContent_EmptyContents_Throws()
    {
        Action act = () => GedArchivePackageBuilder.BuildPackageContent(Request(contents: []));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BuildPackageContent_DuplicateFileName_Throws()
    {
        var contents = new List<ArchiveAttachment>
        {
            new("piece.pdf", "application/pdf", Encoding.UTF8.GetBytes("A")),
            new("piece.pdf", "application/pdf", Encoding.UTF8.GetBytes("B")),
        };

        Action act = () => GedArchivePackageBuilder.BuildPackageContent(Request(contents: contents));

        act.Should().Throw<ArgumentException>().WithMessage("*même nom*");
    }

    [Fact]
    public void BuildPackageManifest_CarriesIndexAxes()
    {
        var axes = new List<ArchiveIndexAxis> { new("numero_lot", "42", IsConfidential: false) };
        GedArchivePackageRequest request = Request(indexAxes: axes);
        ArchivePackageContent content = GedArchivePackageBuilder.BuildPackageContent(request);

        byte[] manifestBytes = GedArchivePackageBuilder.BuildPackageManifest(request, content, ArchivedUtc);

        using var doc = JsonDocument.Parse(manifestBytes);
        JsonElement index = doc.RootElement.GetProperty("index");
        index.GetArrayLength().Should().Be(1);
        JsonElement axis = index[0];
        axis.GetProperty("axisCode").GetString().Should().Be("numero_lot");
        axis.GetProperty("value").GetString().Should().Be("42");
        axis.GetProperty("isConfidential").GetBoolean().Should().BeFalse();

        doc.RootElement.GetProperty("entryKind").GetString().Should().Be("ged-package");
        doc.RootElement.GetProperty("packageHash").GetString().Should().Be(content.PackageHash);
    }

    [Fact]
    public void BuildPackageManifest_ConfidentialAxis_NeverStoresValueInClear_RL19()
    {
        // RL-19 : même si l'appelant fournit par erreur une valeur pour un axe confidentiel, le manifest WORM
        // ne fige JAMAIS cette valeur en clair (défense en profondeur).
        var axes = new List<ArchiveIndexAxis> { new("acheteur", "Dupont SA", IsConfidential: true) };
        GedArchivePackageRequest request = Request(indexAxes: axes);
        ArchivePackageContent content = GedArchivePackageBuilder.BuildPackageContent(request);

        byte[] manifestBytes = GedArchivePackageBuilder.BuildPackageManifest(request, content, ArchivedUtc);

        string json = Encoding.UTF8.GetString(manifestBytes);
        json.Should().NotContain("Dupont SA");

        using var doc = JsonDocument.Parse(manifestBytes);
        JsonElement axis = doc.RootElement.GetProperty("index")[0];
        axis.GetProperty("axisCode").GetString().Should().Be("acheteur");
        axis.GetProperty("isConfidential").GetBoolean().Should().BeTrue();
        axis.GetProperty("value").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
