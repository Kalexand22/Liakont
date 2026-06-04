namespace Liakont.Modules.Ingestion.Tests.Integration;

using System;
using System.IO;
using System.Text;
using FluentAssertions;
using Liakont.Modules.Ingestion.Application;
using Liakont.Modules.Ingestion.Infrastructure;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// Stockage fichier des PDF reçus (PIV04, ADR-0008) : organisation par tenant, adressage déterministe
/// des PDF rattachés, conservation distincte du pool, assainissement anti path-traversal. Pas de base.
/// </summary>
public sealed class IngestedPdfStoreTests : IDisposable
{
    private readonly string _root;
    private readonly FileSystemIngestedPdfStore _store;

    public IngestedPdfStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "liakont-pdf-tests-" + Guid.NewGuid().ToString("N"));
        _store = new FileSystemIngestedPdfStore(
            Options.Create(new IngestionStorageOptions { PdfRootPath = _root }));
    }

    [Fact]
    public async Task Linked_Pdf_Is_Deterministic_And_Overwritten_On_Re_Push()
    {
        var first = await _store.SaveLinkedPdfAsync("tenant-a", "ref-1", Bytes("v1"));
        var second = await _store.SaveLinkedPdfAsync("tenant-a", "ref-1", Bytes("v2"));

        second.Should().Be(first, "le PDF rattaché est adressable de façon déterministe par sa référence source.");
        ReadAll(first).Should().Be("v2", "un re-push écrase le PDF précédent (idempotent).");

        var linkedDir = Path.Combine(_root, "tenant-a", "linked");
        Directory.GetFiles(linkedDir).Should().HaveCount(1);
        ResolvedUnderRoot(first).Should().BeTrue();
    }

    [Fact]
    public async Task Pooled_Pdf_Keeps_Each_Deposit_Distinct()
    {
        var a = await _store.SavePooledPdfAsync("tenant-a", "scan.pdf", Bytes("a"));
        var b = await _store.SavePooledPdfAsync("tenant-a", "scan.pdf", Bytes("b"));

        a.Should().NotBe(b, "chaque dépôt de pool est conservé distinctement (préfixe GUID).");
        var poolDir = Path.Combine(_root, "tenant-a", "pool");
        Directory.GetFiles(poolDir).Should().HaveCount(2);
    }

    [Fact]
    public async Task Tenants_Are_Isolated()
    {
        await _store.SaveLinkedPdfAsync("tenant-a", "ref-1", Bytes("a"));
        await _store.SaveLinkedPdfAsync("tenant-b", "ref-1", Bytes("b"));

        Directory.Exists(Path.Combine(_root, "tenant-a", "linked")).Should().BeTrue();
        Directory.Exists(Path.Combine(_root, "tenant-b", "linked")).Should().BeTrue();
    }

    [Fact]
    public async Task Unsafe_Names_Are_Sanitized_And_Stay_Under_Root()
    {
        var pooled = await _store.SavePooledPdfAsync("tenant-a", "../../etc/passwd", Bytes("x"));

        ResolvedUnderRoot(pooled).Should().BeTrue("aucun segment de chemin fourni n'échappe à la racine.");
        Path.GetFileName(pooled).Should().EndWith("passwd");
        Path.GetFileName(pooled).Should().NotContain("/").And.NotContain("\\");
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    public async Task Dot_Only_Tenant_Segment_Is_Rejected(string tenant)
    {
        // Un segment uniquement composé de points remonterait hors racine via Path.Combine → rejeté.
        var linked = async () => await _store.SaveLinkedPdfAsync(tenant, "ref-1", Bytes("x"));
        var pooled = async () => await _store.SavePooledPdfAsync(tenant, "scan.pdf", Bytes("x"));

        await linked.Should().ThrowAsync<ArgumentException>();
        await pooled.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Tenant_With_Slashes_Is_Sanitized_To_A_Safe_Segment_Under_Root()
    {
        // « ../.. » : les séparateurs sont neutralisés en « _ » → segment sûr, AUCune remontée.
        var pooled = await _store.SavePooledPdfAsync("../..", "scan.pdf", Bytes("x"));

        ResolvedUnderRoot(pooled).Should().BeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static MemoryStream Bytes(string content) => new(Encoding.UTF8.GetBytes(content));

    private string ReadAll(string relativePath) => File.ReadAllText(Path.Combine(_root, relativePath));

    private bool ResolvedUnderRoot(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relativePath));
        var root = Path.GetFullPath(_root);
        return full.StartsWith(root, StringComparison.Ordinal);
    }
}
