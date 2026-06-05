namespace Liakont.Agent.Adapters.EncheresV6.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Liakont.Agent.Adapters.EncheresV6.Tests.Fakes;
using Liakont.Agent.Core.Extraction;
using Xunit;

/// <summary>
/// Tests de la source PDF « dossier de fichiers » d'EncheresV6 (ADP05) : mode lié (GetAttachments par
/// référence), mode pool (ListPoolDocuments sur période), lecture seule stricte, et tolérance aux cas
/// limites (PDF introuvable, dossier absent) — jamais d'échec du run, toujours un Warning. Les PDF de
/// test sont des fichiers factices (le contenu n'est jamais interprété : l'agent transporte le fichier).
/// </summary>
public sealed class FileSystemEncheresV6PdfSourceTests : IDisposable
{
    private static readonly DateTime PeriodFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodTo = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _root;
    private readonly RecordingAgentLog _log = new RecordingAgentLog();

    public FileSystemEncheresV6PdfSourceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "encheresv6-pdf-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Nettoyage best-effort : un fichier resté ouvert ne doit pas faire échouer le test.
        }
    }

    [Fact]
    public void Capabilities_reflect_configured_folders()
    {
        string linked = CreateFolder("linked");
        string pool = CreateFolder("pool");

        new FileSystemEncheresV6PdfSource(new EncheresV6PdfSourceOptions(linkedFolderPath: linked), _log)
            .ProvidesSourceDocuments.Should().BeTrue();
        new FileSystemEncheresV6PdfSource(new EncheresV6PdfSourceOptions(linkedFolderPath: linked), _log)
            .ProvidesUnlinkedDocumentPool.Should().BeFalse();

        new FileSystemEncheresV6PdfSource(new EncheresV6PdfSourceOptions(poolFolderPath: pool), _log)
            .ProvidesUnlinkedDocumentPool.Should().BeTrue();
        new FileSystemEncheresV6PdfSource(new EncheresV6PdfSourceOptions(poolFolderPath: pool), _log)
            .ProvidesSourceDocuments.Should().BeFalse();

        var both = new FileSystemEncheresV6PdfSource(
            new EncheresV6PdfSourceOptions(linkedFolderPath: linked, poolFolderPath: pool), _log);
        both.ProvidesSourceDocuments.Should().BeTrue();
        both.ProvidesUnlinkedDocumentPool.Should().BeTrue();
    }

    [Fact]
    public void GetAttachments_finds_the_pdf_of_a_bordereau_by_its_reference()
    {
        string linked = CreateFolder("linked");
        string pdf = WritePdf(linked, "bordereau-4500.pdf");
        WritePdf(linked, "bordereau-4501.pdf");

        IReadOnlyList<SourceAttachment> attachments = LinkedSource(linked).GetAttachments("no_ba=4500");

        attachments.Should().ContainSingle();
        attachments[0].SourceReference.Should().Be("no_ba=4500");
        attachments[0].FilePath.Should().Be(pdf);
        attachments[0].FileName.Should().Be("bordereau-4500.pdf");
        _log.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void GetAttachments_returns_all_versions_when_multiple_pdfs_match()
    {
        string linked = CreateFolder("linked");
        WritePdf(linked, "4500.pdf");
        WritePdf(linked, "4500-v2.pdf");
        WritePdf(linked, "F-2026_4500_bis.pdf");

        IReadOnlyList<SourceAttachment> attachments = LinkedSource(linked).GetAttachments("no_ba=4500");

        attachments.Select(a => a.FileName)
            .Should().BeEquivalentTo("4500.pdf", "4500-v2.pdf", "F-2026_4500_bis.pdf");
    }

    [Fact]
    public void GetAttachments_does_not_false_match_a_longer_number()
    {
        string linked = CreateFolder("linked");
        WritePdf(linked, "45000.pdf");
        WritePdf(linked, "14500.pdf");

        IReadOnlyList<SourceAttachment> attachments = LinkedSource(linked).GetAttachments("no_ba=4500");

        attachments.Should().BeEmpty("« 4500 » ne doit pas matcher « 45000 » ni « 14500 » (jeton délimité)");
        _log.Warnings.Should().ContainSingle().Which.Should().Contain("introuvable");
    }

    [Fact]
    public void GetAttachments_warns_and_returns_empty_when_pdf_is_absent()
    {
        string linked = CreateFolder("linked");
        WritePdf(linked, "bordereau-9999.pdf");

        IReadOnlyList<SourceAttachment> attachments = LinkedSource(linked).GetAttachments("no_ba=4500");

        attachments.Should().BeEmpty();
        _log.Warnings.Should().ContainSingle().Which.Should().Contain("no_ba=4500");
    }

    [Fact]
    public void GetAttachments_warns_and_returns_empty_when_folder_is_missing()
    {
        string missing = Path.Combine(_root, "does-not-exist");

        IReadOnlyList<SourceAttachment> attachments = LinkedSource(missing).GetAttachments("no_ba=4500");

        attachments.Should().BeEmpty();
        _log.Warnings.Should().ContainSingle().Which.Should().Contain("introuvable");
    }

    [Fact]
    public void GetAttachments_is_empty_without_warning_when_linked_mode_disabled()
    {
        string pool = CreateFolder("pool");
        var source = new FileSystemEncheresV6PdfSource(new EncheresV6PdfSourceOptions(poolFolderPath: pool), _log);

        source.GetAttachments("no_ba=4500").Should().BeEmpty();
        _log.Warnings.Should().BeEmpty("aucune capacité lien déclarée : comportement normal, pas d'alerte");
    }

    [Fact]
    public void GetAttachments_accepts_a_raw_reference_without_prefix()
    {
        string linked = CreateFolder("linked");
        WritePdf(linked, "bordereau-4500.pdf");

        LinkedSource(linked).GetAttachments("4500").Should().ContainSingle();
    }

    [Fact]
    public void GetAttachments_ignores_non_pdf_files_per_search_pattern()
    {
        string linked = CreateFolder("linked");
        File.WriteAllText(Path.Combine(linked, "bordereau-4500.txt"), "pas un pdf");

        LinkedSource(linked).GetAttachments("no_ba=4500").Should().BeEmpty();
    }

    [Fact]
    public void ListPoolDocuments_exposes_pdfs_of_the_folder_in_period()
    {
        string pool = CreateFolder("pool");
        WritePdfAt(pool, "scan-A.pdf", new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc));
        WritePdfAt(pool, "scan-B.pdf", new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc));

        List<PoolDocument> documents = PoolSource(pool).ListPoolDocuments(PeriodFrom, PeriodTo).ToList();

        documents.Select(d => d.FileName).Should().BeEquivalentTo("scan-A.pdf", "scan-B.pdf");
        documents.Select(d => d.PoolReference).Should().BeEquivalentTo("scan-A.pdf", "scan-B.pdf");
        documents.Should().OnlyContain(d => File.Exists(d.FilePath));
    }

    [Fact]
    public void ListPoolDocuments_filters_out_files_outside_the_period()
    {
        string pool = CreateFolder("pool");
        WritePdfAt(pool, "in.pdf", new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc));
        WritePdfAt(pool, "before.pdf", new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc));
        WritePdfAt(pool, "after.pdf", new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));

        List<PoolDocument> documents = PoolSource(pool).ListPoolDocuments(PeriodFrom, PeriodTo).ToList();

        documents.Select(d => d.FileName).Should().ContainSingle().Which.Should().Be("in.pdf");
    }

    [Fact]
    public void ListPoolDocuments_is_empty_when_pool_mode_disabled()
    {
        string linked = CreateFolder("linked");
        var source = new FileSystemEncheresV6PdfSource(new EncheresV6PdfSourceOptions(linkedFolderPath: linked), _log);

        source.ListPoolDocuments(PeriodFrom, PeriodTo).Should().BeEmpty();
        _log.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void ListPoolDocuments_warns_and_returns_empty_when_folder_is_missing()
    {
        string missing = Path.Combine(_root, "no-pool-here");

        PoolSource(missing).ListPoolDocuments(PeriodFrom, PeriodTo).Should().BeEmpty();
        _log.Warnings.Should().ContainSingle().Which.Should().Contain("introuvable");
    }

    [Fact]
    public void Source_is_read_only_it_never_modifies_the_pdf_folder()
    {
        string linked = CreateFolder("linked");
        WritePdfAt(linked, "bordereau-4500.pdf", new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc));
        WritePdfAt(linked, "bordereau-4501.pdf", new DateTime(2026, 1, 11, 0, 0, 0, DateTimeKind.Utc));
        Dictionary<string, (long Length, DateTime WriteUtc)> before = SnapshotFolder(linked);

        var source = new FileSystemEncheresV6PdfSource(
            new EncheresV6PdfSourceOptions(linkedFolderPath: linked, poolFolderPath: linked), _log);
        _ = source.GetAttachments("no_ba=4500");
        _ = source.GetAttachments("no_ba=0000"); // introuvable : ne crée rien
        _ = source.ListPoolDocuments(PeriodFrom, PeriodTo).ToList();

        SnapshotFolder(linked).Should().BeEquivalentTo(before, "la source PDF est en LECTURE SEULE STRICTE");
    }

    [Fact]
    public void Options_normalize_blank_paths_to_disabled_modes()
    {
        var options = new EncheresV6PdfSourceOptions(linkedFolderPath: "   ", poolFolderPath: string.Empty);

        options.LinkedFolderPath.Should().BeNull();
        options.PoolFolderPath.Should().BeNull();
        options.LinkedModeEnabled.Should().BeFalse();
        options.PoolModeEnabled.Should().BeFalse();
    }

    [Fact]
    public void Options_reject_an_empty_search_pattern()
    {
        Action act = () => _ = new EncheresV6PdfSourceOptions(searchPattern: "  ");

        act.Should().Throw<ArgumentException>();
    }

    private static string WritePdf(string folder, string fileName)
    {
        string path = Path.Combine(folder, fileName);
        File.WriteAllText(path, "%PDF-1.4 fictif " + fileName);
        return path;
    }

    private static string WritePdfAt(string folder, string fileName, DateTime lastWriteUtc)
    {
        string path = WritePdf(folder, fileName);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }

    private static Dictionary<string, (long Length, DateTime WriteUtc)> SnapshotFolder(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(
                p => Path.GetFileName(p),
                p => (new FileInfo(p).Length, File.GetLastWriteTimeUtc(p)),
                StringComparer.Ordinal);

    private FileSystemEncheresV6PdfSource LinkedSource(string folder) =>
        new FileSystemEncheresV6PdfSource(new EncheresV6PdfSourceOptions(linkedFolderPath: folder), _log);

    private FileSystemEncheresV6PdfSource PoolSource(string folder) =>
        new FileSystemEncheresV6PdfSource(new EncheresV6PdfSourceOptions(poolFolderPath: folder), _log);

    private string CreateFolder(string name)
    {
        string path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }
}
