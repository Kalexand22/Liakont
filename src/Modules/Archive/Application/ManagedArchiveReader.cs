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
    // exception à laisser remonter — c'est à l'appelant de la traduire en verdict d'intégrité. Le contrat couvre
    // l'ENSEMBLE du parcours manifest→layout→pièces : JSON invalide (JsonException), valeur non-chaîne là où on
    // attend une chaîne (InvalidOperationException sur GetString), segment qui s'assainit en vide dans
    // SanitizeSegment (ArgumentException), date/nombre illisible (FormatException/OverflowException) → tous
    // rendent false (⇒ verdict Altered, fail-closed), jamais une exception jusqu'à la fiche.
    private static bool TryParseManifest(byte[] manifestBytes, out (string PackageHash, string PackageDirectory, IReadOnlyList<string> FileNames) result)
    {
        result = default;
        try
        {
            using var document = JsonDocument.Parse(manifestBytes);
            JsonElement root = document.RootElement;

            // packageHash : chaîne OPTIONNELLE. Présente mais non-chaîne (nombre, objet, …) = manifest corrompu →
            // fail-closed, jamais une InvalidOperationException (GetString sur un token non-chaîne) remontée.
            string packageHash = string.Empty;
            if (root.TryGetProperty("packageHash", out JsonElement packageHashElement))
            {
                if (packageHashElement.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                packageHash = packageHashElement.GetString() ?? string.Empty;
            }

            if (!root.TryGetProperty("archiveKind", out JsonElement archiveKindElement) || archiveKindElement.GetString() is not { } archiveKind
                || !root.TryGetProperty("archiveKey", out JsonElement archiveKeyElement) || archiveKeyElement.GetString() is not { } archiveKey
                || !root.TryGetProperty("filedOn", out JsonElement filedOnElement) || filedOnElement.GetString() is not { } filedOnText
                || !DateOnly.TryParseExact(filedOnText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly filedOn))
            {
                return false;
            }

            // Reconstruit le répertoire du paquet. SanitizeSegment LÈVE (ArgumentException) si archiveKind/archiveKey
            // s'assainit en vide (« archiveKey":"" », « archiveKind":"/" ») : rattrapé par le catch large ci-dessous.
            string packageDirectory = GedArchivePackageLayout.PackageDirectory(
                archiveKind, filedOn.Year, filedOn.Month, archiveKey);

            var fileNames = new List<string>();
            if (root.TryGetProperty("files", out JsonElement filesElement) && filesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement file in filesElement.EnumerateArray())
                {
                    // Une pièce doit porter un nom exploitable (chaîne non vide/non blanche). Sinon = manifest
                    // corrompu → fail-closed : et SURTOUT jamais un nom vide propagé jusqu'à ArchivePackageLayout.Combine
                    // (appelé HORS du try de VerifyManagedPackageAsync), où il exploserait sur la fiche.
                    if (file.ValueKind != JsonValueKind.Object
                        || !file.TryGetProperty("name", out JsonElement nameElement)
                        || nameElement.ValueKind != JsonValueKind.String)
                    {
                        return false;
                    }

                    string? name = nameElement.GetString();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        return false;
                    }

                    // Pré-vol de l'assainissement que Combine appliquera à la re-lecture : un nom qui s'assainit en
                    // vide (« / », « .. ») LÈVERAIT dans Combine (hors try de l'appelant). On le rattrape ICI, dans
                    // le try, pour garantir que Combine ne peut plus jamais lever pour ce manifest.
                    _ = ArchivePackageLayout.SanitizeSegment(name);

                    fileNames.Add(name);
                }
            }

            result = (packageHash, packageDirectory, fileNames);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException or FormatException or OverflowException)
        {
            // Corruption du manifest / du parcours manifest→layout→pièces (réalité opérationnelle, INV-ARCH-GED-2).
            // Fail-closed : ne jamais rendre Verified sur un doute, ne jamais laisser l'exception atteindre la fiche.
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
