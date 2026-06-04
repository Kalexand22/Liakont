namespace Liakont.Modules.Archive.Application;

using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Archive.Domain;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Implémentation du module Archive (TRK05) : composition du paquet WORM, écriture write-once dans le
/// coffre (<see cref="IArchiveStore"/>), scellement de l'entrée dans la chaîne du tenant
/// (<see cref="IArchiveEntryStore"/>), addenda chaînés et vérification d'intégrité. Tenant-scopé : le
/// coffre est rooté sur le tenant courant, la base route vers la base du tenant courant (blueprint §7).
///
/// Ordre garanti sans orphelins : (1) fichiers de contenu écrits dans le coffre (idempotents), (2)
/// réservation de la ligne de chaîne sous verrou/transaction (idempotente par packagePath), (3) manifest
/// écrit APRÈS commit, dérivé du (chain_hash, archived_utc) committé — rejouable à l'identique.
/// </summary>
public sealed class ArchiveService : IArchiveService
{
    private readonly IArchiveStore _store;
    private readonly IArchiveEntryStore _entryStore;
    private readonly ITenantContext _tenantContext;

    public ArchiveService(IArchiveStore store, IArchiveEntryStore entryStore, ITenantContext tenantContext)
    {
        _store = store;
        _entryStore = entryStore;
        _tenantContext = tenantContext;
    }

    public async Task<ArchivePackageResult> ArchiveIssuedDocumentAsync(
        ArchivePackageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string tenant = RequireTenant();
        ValidatePackageRequest(request);

        ArchivePackageContent content = ArchivePackageBuilder.BuildPackageContent(request);
        string packageDir = ArchivePackageLayout.PackageDirectory(
            request.IssueDate.Year,
            request.IssueDate.Month,
            request.DocumentNumber);
        string manifestPath = ArchivePackageLayout.Combine(packageDir, ArchivePackageLayout.ManifestFileName);

        // 1. Fichiers de contenu — idempotents (write-once), HORS verrou/transaction.
        foreach (ArchiveFile file in content.ContentFiles)
        {
            await _store.WriteAsync(tenant, ArchivePackageLayout.Combine(packageDir, file.Name), file.Content, cancellationToken);
        }

        // 2. Réservation de la ligne de chaîne, idempotente par manifestPath.
        ArchiveEntryRecord record = await _entryStore.ReserveAsync(
            request.DocumentId,
            manifestPath,
            content.PackageHash,
            cancellationToken);

        // 3. Manifest APRÈS commit, déterministe depuis la ligne committée.
        byte[] manifest = ArchivePackageBuilder.BuildPackageManifest(
            request, content, new ArchiveSealContext(record.ChainHash, record.ArchivedUtc));
        await _store.WriteAsync(tenant, manifestPath, manifest, cancellationToken);

        return ToResult(record);
    }

    public async Task<ArchivePackageResult> AddAddendumAsync(
        ArchiveAddendumRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string tenant = RequireTenant();

        ArchivePackageContent content = ArchivePackageBuilder.BuildAddendumContent(request);
        ArchiveFile logicalFile = content.ContentFiles[0];
        string packageDir = ArchivePackageLayout.PackageDirectory(
            request.IssueDate.Year,
            request.IssueDate.Month,
            request.DocumentNumber);

        // Chemin dérivé du hash de contenu : déterministe, anti-collision, idempotent (pas de sondage).
        string hashPrefix = content.PackageHash[..16];
        string storedName = ArchivePackageLayout.AddendumDataFileName(hashPrefix, logicalFile.Name);
        string manifestPath = ArchivePackageLayout.Combine(packageDir, ArchivePackageLayout.AddendumManifestFileName(hashPrefix));

        // 1. Fichier de données — idempotent (write-once), HORS verrou/transaction.
        await _store.WriteAsync(tenant, ArchivePackageLayout.Combine(packageDir, storedName), logicalFile.Content, cancellationToken);

        // 2. Réservation de la ligne de chaîne, idempotente par manifestPath.
        ArchiveEntryRecord record = await _entryStore.ReserveAsync(
            request.DocumentId,
            manifestPath,
            content.PackageHash,
            cancellationToken);

        // 3. Manifest APRÈS commit, déterministe depuis la ligne committée.
        var storedFile = new ArchiveFile(storedName, logicalFile.ContentType, logicalFile.Content, logicalFile.Sha256);
        byte[] manifest = ArchivePackageBuilder.BuildAddendumManifest(
            request, storedFile, content.PackageHash, new ArchiveSealContext(record.ChainHash, record.ArchivedUtc));
        await _store.WriteAsync(tenant, manifestPath, manifest, cancellationToken);

        return ToResult(record);
    }

    public async Task<ArchiveIntegrityReport> VerifyTenantChainAsync(CancellationToken cancellationToken = default)
    {
        string tenant = RequireTenant();
        IReadOnlyList<ArchiveEntryRecord> chain = await _entryStore.GetChainAsync(cancellationToken);

        var entries = new List<ArchiveIntegrityEntry>(chain.Count);
        string? recomputedPreviousChain = HashChain.Genesis;
        string? firstBreak = null;

        foreach (ArchiveEntryRecord record in chain)
        {
            VerifiedEntry verified = await VerifyEntryAsync(tenant, record, recomputedPreviousChain, cancellationToken);
            entries.Add(verified.Entry);
            recomputedPreviousChain = verified.RecomputedChainHash;

            if (!verified.Entry.ContentValid || !verified.Entry.ChainValid)
            {
                firstBreak ??= verified.Entry.Detail
                    ?? $"Entrée {verified.Entry.EntryId} altérée ({verified.Entry.PackagePath}).";
            }
        }

        bool intact = firstBreak is null;
        return new ArchiveIntegrityReport(intact, chain.Count, entries, firstBreak);
    }

    private static void ValidatePackageRequest(ArchivePackageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PayloadJson))
        {
            throw new ArgumentException("Le payload transmis à la PA est obligatoire pour l'archivage.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.PaResponseJson))
        {
            throw new ArgumentException("La réponse de la PA est obligatoire pour l'archivage.", nameof(request));
        }

        if (request.PaInvoice is null && string.IsNullOrWhiteSpace(request.PaInvoiceAbsenceReason))
        {
            throw new ArgumentException(
                "La facture PA est absente : son motif d'absence est obligatoire (jamais une absence silencieuse).",
                nameof(request));
        }

        if (request.SourceDocument is null && string.IsNullOrWhiteSpace(request.SourceDocumentAbsenceReason))
        {
            throw new ArgumentException(
                "Le bordereau source est absent : son motif d'absence est obligatoire (jamais une absence silencieuse).",
                nameof(request));
        }
    }

    private static ManifestView ParseManifest(byte[] manifestBytes)
    {
        using var document = JsonDocument.Parse(manifestBytes);
        JsonElement root = document.RootElement;

        bool isAddendum = root.TryGetProperty("entryKind", out JsonElement kind)
            && string.Equals(kind.GetString(), "addendum", StringComparison.Ordinal);

        var fileNames = new List<string>();
        if (root.TryGetProperty("files", out JsonElement files) && files.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement file in files.EnumerateArray())
            {
                if (file.TryGetProperty("name", out JsonElement name) && name.GetString() is { } value)
                {
                    fileNames.Add(value);
                }
            }
        }

        return new ManifestView(isAddendum, fileNames);
    }

    private static string DirectoryOf(string relativePath)
    {
        int lastSlash = relativePath.LastIndexOf('/');
        return lastSlash >= 0 ? relativePath[..(lastSlash + 1)] : string.Empty;
    }

    private static ArchivePackageResult ToResult(ArchiveEntryRecord record) =>
        new(record.EntryId, record.DocumentId, record.PackagePath, record.PackageHash, record.ChainHash, record.ArchivedUtc);

    private async Task<VerifiedEntry> VerifyEntryAsync(
        string tenant,
        ArchiveEntryRecord record,
        string? recomputedPreviousChain,
        CancellationToken cancellationToken)
    {
        try
        {
            byte[] manifestBytes = await _store.ReadAsync(tenant, record.PackagePath, cancellationToken);
            ManifestView manifest = ParseManifest(manifestBytes);

            string directory = DirectoryOf(record.PackagePath);
            var fingerprints = new List<ArchiveFileFingerprint>(manifest.FileNames.Count);
            foreach (string fileName in manifest.FileNames)
            {
                byte[] fileContent = await _store.ReadAsync(tenant, directory + fileName, cancellationToken);
                fingerprints.Add(new ArchiveFileFingerprint(fileName, Sha256Hex.OfBytes(fileContent)));
            }

            string recomputedEntryHash = manifest.IsAddendum && fingerprints.Count == 1
                ? fingerprints[0].Sha256
                : PackageHasher.Compute(fingerprints);

            bool contentValid = string.Equals(recomputedEntryHash, record.PackageHash, StringComparison.Ordinal);
            string recomputedChain = HashChain.Next(recomputedPreviousChain, recomputedEntryHash);
            bool chainValid = string.Equals(recomputedChain, record.ChainHash, StringComparison.Ordinal);

            string? detail = (contentValid, chainValid) switch
            {
                (false, _) => $"Contenu altéré : l'empreinte recalculée ne correspond pas à l'empreinte scellée ({record.PackagePath}).",
                (true, false) => $"Chaînage rompu en amont : l'entrée {record.EntryId} suit une entrée altérée ({record.PackagePath}).",
                _ => null,
            };

            var entry = new ArchiveIntegrityEntry(record.EntryId, record.DocumentId, record.PackagePath, contentValid, chainValid, detail);
            return new VerifiedEntry(entry, recomputedChain);
        }
        catch (ArchiveObjectNotFoundException notFound)
        {
            // Une pièce manquante rompt la chaîne à partir de cette entrée (chaînage propagé invalide).
            string recomputedChain = HashChain.Next(recomputedPreviousChain, "absent:" + record.EntryId.ToString("N"));
            var entry = new ArchiveIntegrityEntry(record.EntryId, record.DocumentId, record.PackagePath, ContentValid: false, ChainValid: false, notFound.Message);
            return new VerifiedEntry(entry, recomputedChain);
        }
        catch (JsonException jsonError)
        {
            string recomputedChain = HashChain.Next(recomputedPreviousChain, "corrupt:" + record.EntryId.ToString("N"));
            var entry = new ArchiveIntegrityEntry(record.EntryId, record.DocumentId, record.PackagePath, ContentValid: false, ChainValid: false, $"Manifest illisible : {jsonError.Message}");
            return new VerifiedEntry(entry, recomputedChain);
        }
    }

    private string RequireTenant()
    {
        if (!_tenantContext.IsResolved || string.IsNullOrWhiteSpace(_tenantContext.TenantId))
        {
            throw new InvalidOperationException(
                "Le module Archive est tenant-scopé : aucun tenant résolu pour cette opération (blueprint §7).");
        }

        return ArchivePackageLayout.SanitizeSegment(_tenantContext.TenantId);
    }

    private sealed record VerifiedEntry(ArchiveIntegrityEntry Entry, string RecomputedChainHash);

    private sealed record ManifestView(bool IsAddendum, IReadOnlyList<string> FileNames);
}
