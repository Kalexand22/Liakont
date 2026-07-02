namespace Liakont.Modules.Archive.Application;

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Archive.Domain;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Implémentation de la surface d'archivage GÉNÉRIQUE (F19 §5.1, option C, RL-05). Range un document GED
/// write-once via <see cref="IArchiveStore"/> sous <c>_ged/{kind}/{année}/{mois}/{clé}/</c>. N'a AUCUNE
/// dépendance vers la chaîne fiscale (<c>IArchiveEntryStore</c>) : un document GED-seul ne crée
/// STRUCTURELLEMENT aucune ligne <c>documents.archive_entries</c> (INV-ARCH-GED-1, hash-neutralité facture).
/// Tenant-scopé : le coffre est rooté sur le tenant courant (blueprint §7).
///
/// Idempotence : le paquet est déterministe (contenu → empreinte). Un manifest DÉJÀ présent = re-rangement
/// no-op (on relit son horodatage sans réécrire, car le manifest porte un horodatage volatil qui, réécrit,
/// diffèrerait du scellé et lèverait un conflit WORM). Une écriture concurrente qui perd la course
/// (<see cref="ArchiveWriteConflictException"/> sur le manifest) est traitée comme « déjà rangé » — les
/// pièces de contenu, elles, sont déterministes et ré-écrites idempotemment.
/// </summary>
public sealed class GenericArchiveService : IGenericArchiveService
{
    private readonly IArchiveStore _store;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public GenericArchiveService(IArchiveStore store, ITenantContext tenantContext)
        : this(store, tenantContext, TimeProvider.System)
    {
    }

    public GenericArchiveService(IArchiveStore store, ITenantContext tenantContext, TimeProvider timeProvider)
    {
        _store = store;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider;
    }

    public async Task<GedArchivePackageResult> ArchiveManagedDocumentAsync(
        GedArchivePackageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string tenant = RequireTenant();

        ArchivePackageContent content = GedArchivePackageBuilder.BuildPackageContent(request);
        string packageDir = GedArchivePackageLayout.PackageDirectory(
            request.ArchiveKind, request.FiledOn.Year, request.FiledOn.Month, request.ArchiveKey);
        string manifestPath = ArchivePackageLayout.Combine(packageDir, GedArchivePackageLayout.ManifestFileName);

        // Re-rangement idempotent : un manifest déjà présent = paquet déjà rangé (no-op, jamais une réécriture).
        if (await _store.ExistsAsync(tenant, manifestPath, cancellationToken))
        {
            return await ReadExistingAsync(tenant, manifestPath, content.PackageHash, cancellationToken);
        }

        // 1. Pièces de contenu — idempotentes (write-once), déterministes.
        foreach (ArchiveFile file in content.ContentFiles)
        {
            await _store.WriteAsync(tenant, ArchivePackageLayout.Combine(packageDir, file.Name), file.Content, cancellationToken);
        }

        // 2. Manifest horodaté — sur course concurrente perdue, on relit le paquet du gagnant.
        DateTimeOffset archivedUtc = _timeProvider.GetUtcNow();
        byte[] manifest = GedArchivePackageBuilder.BuildPackageManifest(request, content, archivedUtc);
        try
        {
            await _store.WriteAsync(tenant, manifestPath, manifest, cancellationToken);
        }
        catch (ArchiveWriteConflictException)
        {
            return await ReadExistingAsync(tenant, manifestPath, content.PackageHash, cancellationToken);
        }

        return new GedArchivePackageResult(manifestPath, content.PackageHash, archivedUtc, AlreadyArchived: false);
    }

    public async Task<GedArchivePackageResult> AddManagedAddendumAsync(
        GedArchiveAddendumRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string tenant = RequireTenant();

        ArchivePackageContent content = GedArchivePackageBuilder.BuildAddendumContent(request);
        ArchiveFile logicalFile = content.ContentFiles[0];
        string packageDir = GedArchivePackageLayout.PackageDirectory(
            request.ArchiveKind, request.FiledOn.Year, request.FiledOn.Month, request.ArchiveKey);

        // Clé d'idempotence indexée sur (Kind, nom de pièce, CONTENU) — les TROIS composantes qui déterminent
        // aussi l'empreinte scellée (via le nom stocké) : « même identité ⟺ même chemin ⟺ même scellé ». Deux
        // addenda aux octets identiques mais de Kind OU de nom distinct sont des pièces probantes distinctes,
        // rangées séparément (jamais un AlreadyArchived silencieux qui perdrait le Kind, ni un conflit WORM sur
        // un scellé divergent). Composantes hachées (longueur fixe 64 hex) → clé sans ambiguïté de délimiteur.
        // GDF11 finding 2.
        string idempotencyKey = Sha256Hex.OfString(
            Sha256Hex.OfString(request.Kind ?? string.Empty)
            + Sha256Hex.OfString(logicalFile.Name)
            + content.PackageHash);
        string keyPrefix = idempotencyKey[..16];
        string storedName = ArchivePackageLayout.AddendumDataFileName(keyPrefix, logicalFile.Name);
        string manifestPath = ArchivePackageLayout.Combine(packageDir, ArchivePackageLayout.AddendumManifestFileName(keyPrefix));

        var storedFile = new ArchiveFile(storedName, logicalFile.ContentType, logicalFile.Content, logicalFile.Sha256);

        // Empreinte SCELLÉE = empreinte de PAQUET (nom+sha) sur la pièce réellement stockée — MÊME primitive que
        // le paquet, pour que VerifyManagedPackageAsync recalcule et compare UNIFORMÉMENT (aucune branche par type
        // de manifest, jamais un faux « Altered » sur un manifest d'addendum). GDF11 finding 3.
        string sealedHash = PackageHasher.Compute([new ArchiveFileFingerprint(storedFile.Name, storedFile.Sha256)]);

        if (await _store.ExistsAsync(tenant, manifestPath, cancellationToken))
        {
            return await ReadExistingAsync(tenant, manifestPath, sealedHash, cancellationToken);
        }

        await _store.WriteAsync(tenant, ArchivePackageLayout.Combine(packageDir, storedName), logicalFile.Content, cancellationToken);

        DateTimeOffset archivedUtc = _timeProvider.GetUtcNow();
        byte[] manifest = GedArchivePackageBuilder.BuildAddendumManifest(request, storedFile, sealedHash, archivedUtc);
        try
        {
            await _store.WriteAsync(tenant, manifestPath, manifest, cancellationToken);
        }
        catch (ArchiveWriteConflictException)
        {
            return await ReadExistingAsync(tenant, manifestPath, sealedHash, cancellationToken);
        }

        return new GedArchivePackageResult(manifestPath, sealedHash, archivedUtc, AlreadyArchived: false);
    }

    private static ManifestSeal ReadManifestSeal(byte[] manifestBytes)
    {
        using var document = JsonDocument.Parse(manifestBytes);
        string packageHash = document.RootElement.TryGetProperty("packageHash", out JsonElement packageHashElement)
            ? packageHashElement.GetString() ?? string.Empty
            : string.Empty;

        // Horodatage absent/illisible : on laisse la valeur par défaut, on ne fabrique pas de fausse date.
        DateTimeOffset archivedUtc = default;
        if (document.RootElement.TryGetProperty("archivedUtc", out JsonElement archivedUtcElement)
            && archivedUtcElement.TryGetDateTimeOffset(out DateTimeOffset value))
        {
            archivedUtc = value;
        }

        return new ManifestSeal(packageHash, archivedUtc);
    }

    private async Task<GedArchivePackageResult> ReadExistingAsync(string tenant, string manifestPath, string expectedPackageHash, CancellationToken cancellationToken)
    {
        byte[] manifestBytes = await _store.ReadAsync(tenant, manifestPath, cancellationToken);
        ManifestSeal seal = ReadManifestSeal(manifestBytes);
        if (!string.Equals(seal.PackageHash, expectedPackageHash, StringComparison.Ordinal))
        {
            // Même clé (ArchiveKind/ArchiveKey/FiledOn), contenu différent : WORM — on bloque plutôt que
            // de reporter un content_hash qui ne correspond pas au paquet réellement rangé (F19 §3.4.1).
            throw ArchiveWriteConflictException.ForPath(manifestPath);
        }

        return new GedArchivePackageResult(manifestPath, expectedPackageHash, seal.ArchivedUtc, AlreadyArchived: true);
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

    private readonly record struct ManifestSeal(string PackageHash, DateTimeOffset ArchivedUtc);
}
