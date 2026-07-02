namespace Liakont.Modules.Archive.Application;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Archive.Domain;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Lecture d'un paquet GED du coffre WORM (F19 §6.7, option C), sœur en lecture de
/// <see cref="GenericArchiveService"/>. RE-LIT les octets réels via <see cref="IArchiveStore"/> et recalcule
/// leur empreinte avec les MÊMES primitives que l'écriture (<see cref="Sha256Hex"/> par pièce,
/// <see cref="PackageHasher"/> pour le paquet) — l'ancre d'intégrité est le contenu du coffre, pas une valeur
/// en base (INV-ARCH-GED-2). Tenant-scopé : le coffre est rooté sur le tenant courant (blueprint §7). Le
/// répertoire du paquet est reconstruit depuis les champs du manifest (kind/clé/date), jamais par découpage de
/// chaîne du chemin (robuste à l'assainissement, cohérent avec <see cref="GedArchivePackageLayout"/>).
/// </summary>
public sealed class ManagedArchiveReader : IManagedArchiveReader
{
    private readonly IArchiveStore _store;
    private readonly ITenantContext _tenantContext;

    public ManagedArchiveReader(IArchiveStore store, ITenantContext tenantContext)
    {
        _store = store;
        _tenantContext = tenantContext;
    }

    public async Task<GedArchiveIntegrityResult> VerifyManagedPackageAsync(
        string? manifestPath,
        string? indexedContentHash,
        CancellationToken cancellationToken = default)
    {
        // Rien à vérifier : document non (encore) rangé dans le coffre.
        if (string.IsNullOrWhiteSpace(manifestPath) || string.IsNullOrWhiteSpace(indexedContentHash))
        {
            return new GedArchiveIntegrityResult(GedArchiveIntegrityStatus.NotArchived, indexedContentHash, null, null);
        }

        string tenant = RequireTenant();

        if (!await _store.ExistsAsync(tenant, manifestPath, cancellationToken))
        {
            return new GedArchiveIntegrityResult(
                GedArchiveIntegrityStatus.Missing,
                indexedContentHash,
                null,
                "Le paquet d'archive de ce document est introuvable dans le coffre.");
        }

        byte[] manifestBytes = await _store.ReadAsync(tenant, manifestPath, cancellationToken);
        if (!TryParseManifest(manifestBytes, out (string PackageHash, string PackageDirectory, IReadOnlyList<string> FileNames) manifest))
        {
            return new GedArchiveIntegrityResult(
                GedArchiveIntegrityStatus.Altered,
                indexedContentHash,
                null,
                "Le manifest du paquet est illisible ou corrompu : l'intégrité ne peut pas être confirmée.");
        }

        (string sealedPackageHash, string packageDirectory, IReadOnlyList<string> fileNames) = manifest;

        if (fileNames.Count == 0)
        {
            return new GedArchiveIntegrityResult(
                GedArchiveIntegrityStatus.Missing,
                indexedContentHash,
                null,
                "Le manifest du paquet ne référence aucune pièce de contenu.");
        }

        // RE-LECTURE des octets réels de chaque pièce + recalcul de son empreinte (jamais l'empreinte du manifest).
        // Chaque vérification relit en mémoire l'intégralité des octets des pièces sans borne ni cache —
        // acceptable pour une console opérateur peu sollicitée ; un hachage en flux (streaming) est l'échappatoire
        // si des paquets lourds sont attendus.
        var fingerprints = new List<ArchiveFileFingerprint>(fileNames.Count);
        foreach (string name in fileNames)
        {
            string filePath = ArchivePackageLayout.Combine(packageDirectory, name);
            if (!await _store.ExistsAsync(tenant, filePath, cancellationToken))
            {
                return new GedArchiveIntegrityResult(
                    GedArchiveIntegrityStatus.Missing,
                    indexedContentHash,
                    null,
                    $"Une pièce du paquet est introuvable dans le coffre : « {name} ».");
            }

            byte[] fileBytes = await _store.ReadAsync(tenant, filePath, cancellationToken);
            fingerprints.Add(new ArchiveFileFingerprint(name, Sha256Hex.OfBytes(fileBytes)));
        }

        string recomputed = PackageHasher.Compute(fingerprints);
        bool verified = string.Equals(recomputed, indexedContentHash, StringComparison.Ordinal);
        if (verified)
        {
            return new GedArchiveIntegrityResult(GedArchiveIntegrityStatus.Verified, indexedContentHash, recomputed, null);
        }

        // Divergence : distinguer un index désynchronisé (les octets sont conformes au manifest scellé) d'une
        // altération réelle des octets (le manifest lui-même ne correspond plus).
        string detail = string.Equals(recomputed, sealedPackageHash, StringComparison.Ordinal)
            ? "L'empreinte indexée en base diffère de l'empreinte scellée du paquet (index à resynchroniser)."
            : "Le contenu du paquet a été modifié depuis son scellement (empreinte recalculée divergente).";
        return new GedArchiveIntegrityResult(GedArchiveIntegrityStatus.Altered, indexedContentHash, recomputed, detail);
    }

    public async Task<string?> ReadManagedReadableHtmlAsync(
        string? manifestPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            return null;
        }

        string tenant = RequireTenant();
        if (!await _store.ExistsAsync(tenant, manifestPath, cancellationToken))
        {
            return null;
        }

        byte[] manifestBytes = await _store.ReadAsync(tenant, manifestPath, cancellationToken);
        if (!TryParseManifest(manifestBytes, out (string PackageHash, string PackageDirectory, IReadOnlyList<string> FileNames) manifest))
        {
            return null;
        }

        string packageDirectory = manifest.PackageDirectory;
        string htmlPath = ArchivePackageLayout.Combine(packageDirectory, GedArchivePackageLayout.ReadableHtmlFileName);
        if (!await _store.ExistsAsync(tenant, htmlPath, cancellationToken))
        {
            return null;
        }

        byte[] htmlBytes = await _store.ReadAsync(tenant, htmlPath, cancellationToken);
        return Encoding.UTF8.GetString(htmlBytes);
    }

    // Extrait du manifest l'empreinte scellée, le répertoire du paquet (reconstruit depuis kind/clé/date) et les
    // noms des pièces de contenu. Le répertoire est reconstruit via GedArchivePackageLayout (mêmes règles
    // d'assainissement qu'à l'écriture), jamais par découpage de chaîne du chemin de manifest. Défensif : un
    // manifest corrompu ou tronqué dans le coffre est une réalité opérationnelle (INV-ARCH-GED-2), pas une
    // exception à laisser remonter — c'est à l'appelant de la traduire en verdict d'intégrité.
    private static bool TryParseManifest(byte[] manifestBytes, out (string PackageHash, string PackageDirectory, IReadOnlyList<string> FileNames) result)
    {
        result = default;
        try
        {
            using var document = JsonDocument.Parse(manifestBytes);
            JsonElement root = document.RootElement;

            string packageHash = root.TryGetProperty("packageHash", out JsonElement packageHashElement)
                ? packageHashElement.GetString() ?? string.Empty
                : string.Empty;

            if (!root.TryGetProperty("archiveKind", out JsonElement archiveKindElement) || archiveKindElement.GetString() is not { } archiveKind
                || !root.TryGetProperty("archiveKey", out JsonElement archiveKeyElement) || archiveKeyElement.GetString() is not { } archiveKey
                || !root.TryGetProperty("filedOn", out JsonElement filedOnElement) || filedOnElement.GetString() is not { } filedOnText
                || !DateOnly.TryParseExact(filedOnText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly filedOn))
            {
                return false;
            }

            string packageDirectory = GedArchivePackageLayout.PackageDirectory(
                archiveKind, filedOn.Year, filedOn.Month, archiveKey);

            var fileNames = new List<string>();
            if (root.TryGetProperty("files", out JsonElement filesElement) && filesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement file in filesElement.EnumerateArray())
                {
                    if (file.TryGetProperty("name", out JsonElement nameElement) && nameElement.GetString() is { } name)
                    {
                        fileNames.Add(name);
                    }
                }
            }

            result = (packageHash, packageDirectory, fileNames);
            return true;
        }
        catch (JsonException)
        {
            return false;
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
}
