namespace Liakont.Modules.Archive.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Archive.Application;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Archive.Domain;
using Liakont.Modules.Archive.Tests.Unit.Doubles;
using Xunit;

/// <summary>
/// Surface d'archivage GÉNÉRIQUE (GED07, F19 §5.1, option C) : rangement write-once sous _ged/, idempotence,
/// RL-19 (aucune valeur confidentielle en clair dans le coffre), et hash-neutralité STRUCTURELLE de la facture
/// (le service ne dépend pas de la chaîne fiscale).
/// </summary>
public sealed class GenericArchiveServiceTests
{
    private const string Tenant = "acme";

    private readonly InMemoryArchiveStore _store = new();

    private static GedArchivePackageRequest Request(IReadOnlyList<ArchiveIndexAxis>? axes = null) => new(
        ArchiveKind: "bordereau",
        ArchiveKey: "K-42",
        FiledOn: new DateOnly(2026, 5, 12),
        Contents: [new ArchiveAttachment("piece.pdf", "application/pdf", Encoding.UTF8.GetBytes("%PDF-ged"))],
        ReadableHtml: "<p>aperçu</p>",
        IndexAxes: axes ?? []);

    private GenericArchiveService CreateService(string? tenant = Tenant) =>
        new(_store, new StubTenantContext(tenant));

    [Fact]
    public async Task ArchiveManagedDocument_RangesWriteOnceUnderGedPrefix()
    {
        GenericArchiveService service = CreateService();

        GedArchivePackageResult result = await service.ArchiveManagedDocumentAsync(Request());

        result.AlreadyArchived.Should().BeFalse();
        result.ArchivePath.Should().Be("_ged/bordereau/2026/05/K-42/manifest.json");
        result.ContentHash.Should().MatchRegex("^[0-9a-f]{64}$");

        // 1 pièce + 1 aperçu + 1 manifest = 3 objets, tous sous _ged/.
        _store.ObjectCount.Should().Be(3);
        (await _store.ExistsAsync(Tenant, "_ged/bordereau/2026/05/K-42/piece.pdf")).Should().BeTrue();
    }

    [Fact]
    public async Task ArchiveManagedDocument_IsIdempotent_OnReplay()
    {
        GenericArchiveService service = CreateService();

        GedArchivePackageResult first = await service.ArchiveManagedDocumentAsync(Request());
        int afterFirst = _store.ObjectCount;
        GedArchivePackageResult second = await service.ArchiveManagedDocumentAsync(Request());

        second.AlreadyArchived.Should().BeTrue();
        second.ArchivePath.Should().Be(first.ArchivePath);
        second.ContentHash.Should().Be(first.ContentHash);
        _store.ObjectCount.Should().Be(afterFirst, "un re-rangement ne réécrit rien (write-once)");
    }

    [Fact]
    public async Task ArchiveManagedDocument_UnresolvedTenant_Throws()
    {
        GenericArchiveService service = CreateService(tenant: null);

        Func<Task> act = () => service.ArchiveManagedDocumentAsync(Request());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ArchiveManagedDocument_ConfidentialAxisValue_NeverStoredInClear_RL19()
    {
        GenericArchiveService service = CreateService();
        var axes = new List<ArchiveIndexAxis> { new("acheteur", "Dupont SA", IsConfidential: true) };

        GedArchivePackageResult result = await service.ArchiveManagedDocumentAsync(Request(axes));

        byte[] manifest = await _store.ReadAsync(Tenant, result.ArchivePath);
        Encoding.UTF8.GetString(manifest).Should().NotContain("Dupont SA");
    }

    [Fact]
    public async Task ArchiveManagedDocument_SameKeyDifferentContent_ThrowsWormConflict_NotSilentNoOp()
    {
        GenericArchiveService service = CreateService();
        await service.ArchiveManagedDocumentAsync(Request());

        // Même clé (kind/key/filedOn) mais contenu DIFFÉRENT : le chemin est indexé sur la clé, pas sur le
        // contenu. On BLOQUE (WORM) plutôt qu'un no-op silencieux qui reporterait un content_hash ne
        // correspondant pas au paquet réellement rangé (F19 §3.4.1, jamais un écrasement silencieux).
        GedArchivePackageRequest divergent = Request() with
        {
            Contents = [new ArchiveAttachment("piece.pdf", "application/pdf", Encoding.UTF8.GetBytes("%PDF-DIFFERENT"))],
        };

        Func<Task> act = () => service.ArchiveManagedDocumentAsync(divergent);

        await act.Should().ThrowAsync<ArchiveWriteConflictException>();
    }

    [Fact]
    public async Task AddManagedAddendum_RangesAddendumUnderGedPrefix()
    {
        GenericArchiveService service = CreateService();
        await service.ArchiveManagedDocumentAsync(Request());

        var addendum = new GedArchiveAddendumRequest(
            ArchiveKind: "bordereau",
            ArchiveKey: "K-42",
            FiledOn: new DateOnly(2026, 5, 12),
            Kind: "note",
            Attachment: new ArchiveAttachment("note.txt", "text/plain", Encoding.UTF8.GetBytes("addendum")));

        GedArchivePackageResult result = await service.AddManagedAddendumAsync(addendum);

        result.AlreadyArchived.Should().BeFalse();
        result.ArchivePath.Should().StartWith("_ged/bordereau/2026/05/K-42/manifest-addendum-");
        result.ArchivePath.Should().EndWith(".json");

        // Re-ajout du MÊME addendum = idempotent (chemin dérivé du hash de contenu).
        GedArchivePackageResult again = await service.AddManagedAddendumAsync(addendum);
        again.AlreadyArchived.Should().BeTrue();
        again.ArchivePath.Should().Be(result.ArchivePath);
    }

    [Fact]
    public void GenericArchiveService_HasNoFiscalChainDependency_OptionC()
    {
        // Hash-neutralité STRUCTURELLE (INV-ARCH-GED-1) : le service ne peut pas créer de ligne
        // documents.archive_entries car il ne dépend d'AUCUN IArchiveEntryStore (option C, prouvé au type).
        IEnumerable<Type> constructorParameterTypes = typeof(GenericArchiveService)
            .GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType);

        constructorParameterTypes.Should().NotContain(typeof(IArchiveEntryStore));
    }
}
