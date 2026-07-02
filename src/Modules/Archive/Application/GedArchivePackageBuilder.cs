namespace Liakont.Modules.Archive.Application;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Archive.Domain;

/// <summary>
/// Compose les fichiers d'un paquet d'archive GÉNÉRIQUE (GED, F19 §5.1) et son manifest. Pur : aucune E/S,
/// aucune dépendance au coffre ni à la base — il transforme une demande en octets, entièrement testable.
/// Miroir générique d'<see cref="ArchivePackageBuilder"/> (facture). L'empreinte du paquet couvre les fichiers
/// de CONTENU ; le manifest (qui la porte lui-même) en est EXCLU pour éviter toute circularité.
///
/// RL-19 (P1) appliqué STRUCTURELLEMENT : pour un axe confidentiel, le manifest ne fige que le CODE et le
/// caractère confidentiel de l'axe, JAMAIS sa valeur en clair (défense en profondeur — même si l'appelant
/// fournissait par erreur une valeur pour un axe confidentiel).
/// </summary>
public static class GedArchivePackageBuilder
{
    private const string Notice =
        "Coffre WORM Liakont (espace GED) : ce paquet est immuable et rangé write-once, HORS de la chaîne de " +
        "hashes fiscale. Son intégrité est garantie par les empreintes SHA-256 ci-dessous et par le rangement " +
        "write-once du coffre. Ce coffre n'est pas un SAE certifié NF Z42-013.";

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>Compose les fichiers de contenu d'un paquet GED et calcule son empreinte (empreinte de contenu).</summary>
    public static ArchivePackageContent BuildPackageContent(GedArchivePackageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Contents is null || request.Contents.Count == 0)
        {
            throw new ArgumentException("Un paquet d'archive GED doit contenir au moins une pièce.", nameof(request));
        }

        var files = new List<ArchiveFile>(request.Contents.Count + 1);
        foreach (ArchiveAttachment attachment in request.Contents)
        {
            files.Add(CreateFile(
                ArchivePackageLayout.SanitizeSegment(attachment.FileName),
                attachment.ContentType,
                attachment.Content));
        }

        if (!string.IsNullOrWhiteSpace(request.ReadableHtml))
        {
            files.Add(CreateFile(
                GedArchivePackageLayout.ReadableHtmlFileName,
                "text/html; charset=utf-8",
                Encoding.UTF8.GetBytes(request.ReadableHtml)));
        }

        // Noms de fichiers distincts après assainissement : une collision produirait un conflit WORM
        // silencieux (deux pièces au même chemin) — on la refuse explicitement, jamais un écrasement.
        RequireDistinctFileNames(files);

        string packageHash = PackageHasher.Compute(files.Select(f => new ArchiveFileFingerprint(f.Name, f.Sha256)).ToList());
        return new ArchivePackageContent(files, packageHash, []);
    }

    /// <summary>
    /// Compose le fichier unique d'un addendum GED et calcule l'empreinte de son CONTENU (indépendante du nom).
    /// Cette empreinte de contenu est la composante d'IDENTITÉ de la clé d'idempotence de l'addendum (combinée au
    /// <c>Kind</c> par <see cref="GenericArchiveService"/>) ; l'empreinte SCELLÉE dans le manifest, elle, est
    /// l'empreinte de PAQUET sur la pièce stockée (nom+sha), pour une vérification uniforme.
    /// </summary>
    public static ArchivePackageContent BuildAddendumContent(GedArchiveAddendumRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ArchiveFile file = CreateFile(
            ArchivePackageLayout.SanitizeSegment(request.Attachment.FileName),
            request.Attachment.ContentType,
            request.Attachment.Content);

        return new ArchivePackageContent([file], file.Sha256, []);
    }

    /// <summary>Construit le manifest d'un paquet GED (octets UTF-8), avec ses axes d'index (RL-19 appliqué).</summary>
    public static byte[] BuildPackageManifest(GedArchivePackageRequest request, ArchivePackageContent content, DateTimeOffset archivedUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(content);

        var manifest = new JsonObject
        {
            ["schemaVersion"] = "1",
            ["entryKind"] = "ged-package",
            ["archiveKind"] = request.ArchiveKind,
            ["archiveKey"] = request.ArchiveKey,
            ["filedOn"] = request.FiledOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["archivedUtc"] = archivedUtc.ToString("O", CultureInfo.InvariantCulture),
            ["packageHash"] = content.PackageHash,
            ["files"] = FilesNode(content.ContentFiles),
            ["index"] = IndexNode(request.IndexAxes),
            ["notice"] = Notice,
        };
        return Serialize(manifest);
    }

    /// <summary>
    /// Construit le manifest d'un addendum GED (octets UTF-8). <paramref name="addendumHash"/> est l'empreinte
    /// SCELLÉE = empreinte de PAQUET sur la pièce stockée (nom+sha), pour que la vérification recalcule la même
    /// primitive que pour un paquet (comportement de <c>Verify</c> DÉFINI sur un manifest d'addendum). Le
    /// <c>Kind</c> de l'addendum y est figé (métadonnée probante).
    /// </summary>
    public static byte[] BuildAddendumManifest(GedArchiveAddendumRequest request, ArchiveFile storedFile, string addendumHash, DateTimeOffset archivedUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(storedFile);
        ArgumentException.ThrowIfNullOrEmpty(addendumHash);

        var manifest = new JsonObject
        {
            ["schemaVersion"] = "1",
            ["entryKind"] = "ged-addendum",
            ["addendumKind"] = request.Kind,
            ["archiveKind"] = request.ArchiveKind,
            ["archiveKey"] = request.ArchiveKey,
            ["filedOn"] = request.FiledOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["archivedUtc"] = archivedUtc.ToString("O", CultureInfo.InvariantCulture),
            ["packageHash"] = addendumHash,
            ["files"] = FilesNode([storedFile]),
            ["notice"] = Notice,
        };
        return Serialize(manifest);
    }

    private static JsonArray IndexNode(IReadOnlyList<ArchiveIndexAxis> axes)
    {
        var array = new JsonArray();
        if (axes is null)
        {
            return array;
        }

        foreach (ArchiveIndexAxis axis in axes)
        {
            array.Add(new JsonObject
            {
                ["axisCode"] = axis.AxisCode,
                ["isConfidential"] = axis.IsConfidential,

                // RL-19 : jamais une valeur confidentielle en clair dans le manifest WORM (défense en profondeur).
                ["value"] = axis.IsConfidential ? null : axis.Value,
            });
        }

        return array;
    }

    private static JsonArray FilesNode(IReadOnlyList<ArchiveFile> files)
    {
        var array = new JsonArray();
        foreach (ArchiveFile file in files.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            array.Add(new JsonObject
            {
                ["name"] = file.Name,
                ["contentType"] = file.ContentType,
                ["sha256"] = file.Sha256,
                ["sizeBytes"] = file.Content.Length,
            });
        }

        return array;
    }

    private static void RequireDistinctFileNames(IReadOnlyList<ArchiveFile> files)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (ArchiveFile file in files)
        {
            if (!seen.Add(file.Name))
            {
                throw new ArgumentException(
                    $"Deux pièces du paquet GED portent le même nom après assainissement (« {file.Name} ») : " +
                    "chaque pièce doit avoir un nom distinct (jamais un écrasement silencieux).",
                    nameof(files));
            }
        }
    }

    private static ArchiveFile CreateFile(string name, string contentType, byte[] content) =>
        new(name, contentType, content, Sha256Hex.OfBytes(content));

    private static byte[] Serialize(JsonObject manifest) =>
        Encoding.UTF8.GetBytes(manifest.ToJsonString(ManifestJsonOptions));
}
