namespace Liakont.Modules.Archive.Application;

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Archive.Domain;

/// <summary>
/// Compose les fichiers d'un paquet d'archive et son manifest (TRK05 §2). Pur : aucune E/S, aucune
/// dépendance au coffre ni à la base — il transforme une demande en octets, ce qui le rend entièrement
/// testable. L'empreinte de paquet (entry_hash) couvre les fichiers de CONTENU ; le manifest (qui porte
/// lui-même cette empreinte et le <c>chain_hash</c>) en est exclu pour éviter toute circularité.
/// </summary>
public static class ArchivePackageBuilder
{
    private const string Notice =
        "Coffre WORM Liakont : ce paquet est immuable. Son intégrité est garantie par les empreintes " +
        "SHA-256 ci-dessous et par le chaînage des entrées (chain_hash). Toute altération d'une pièce " +
        "rompt la chaîne à partir de cette entrée. Ce coffre n'est pas un SAE certifié NF Z42-013.";

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>Compose les fichiers de contenu d'un paquet de document émis et calcule son empreinte.</summary>
    public static ArchivePackageContent BuildPackageContent(ArchivePackageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var files = new List<ArchiveFile>
        {
            CreateFile(ArchivePackageLayout.PayloadFileName, "application/json", Encoding.UTF8.GetBytes(request.PayloadJson)),
            CreateFile(ArchivePackageLayout.PaResponseFileName, "application/json", Encoding.UTF8.GetBytes(request.PaResponseJson)),
            CreateFile(ArchivePackageLayout.ReadableHtmlFileName, "text/html; charset=utf-8", ReadableDocumentRenderer.Render(request.Readable)),
        };

        var absent = new List<ArchiveAbsentPiece>();
        AddOptionalPiece(files, absent, "facture-pa", request.PaInvoice, request.PaInvoiceAbsenceReason);
        AddOptionalPiece(files, absent, "bordereau-source", request.SourceDocument, request.SourceDocumentAbsenceReason);

        // Métadonnées d'audit dans un fichier haché (INV-ARCHIVE-002) : mappingTrace + absentPieces couverts par packageHash.
        byte[] metadataBytes = BuildArchiveMetadataBytes(request.DocumentId, request.DocumentNumber, request.IssueDate, request.MappingTraceJson, absent);
        files.Add(CreateFile("archive-metadata.json", "application/json", metadataBytes));

        string packageHash = PackageHasher.Compute(files.Select(f => new ArchiveFileFingerprint(f.Name, f.Sha256)).ToList());
        return new ArchivePackageContent(files, packageHash, absent);
    }

    /// <summary>
    /// Compose le fichier unique d'un addendum et calcule son empreinte (addendum_hash). L'empreinte est
    /// l'empreinte du CONTENU (indépendante du nom) : le nom de stockage est dérivé de cette empreinte
    /// (préfixe de hash, anti-collision et déterministe), il ne peut donc pas entrer dans son propre calcul.
    /// </summary>
    public static ArchivePackageContent BuildAddendumContent(ArchiveAddendumRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ArchiveFile file = CreateFile(
            ArchivePackageLayout.SanitizeSegment(request.Attachment.FileName),
            request.Attachment.ContentType,
            request.Attachment.Content);

        return new ArchivePackageContent([file], file.Sha256, []);
    }

    /// <summary>Construit le manifest d'un paquet de document émis (octets UTF-8), scellé avec le contexte de chaîne.</summary>
    public static byte[] BuildPackageManifest(
        ArchivePackageRequest request,
        ArchivePackageContent content,
        ArchiveSealContext seal)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(seal);

        var manifest = BaseManifest("package", request.DocumentId, request.DocumentNumber, request.IssueDate, content.PackageHash, content.ContentFiles, seal);
        return Serialize(manifest);
    }

    /// <summary>
    /// Construit le manifest d'un addendum (octets UTF-8), scellé avec le contexte de chaîne. Le fichier
    /// stocké (<paramref name="storedFile"/>) porte son nom RÉEL (séquencé sous verrou) ; l'empreinte
    /// d'addendum (<paramref name="addendumHash"/>) est l'empreinte du contenu, indépendante du nom.
    /// </summary>
    public static byte[] BuildAddendumManifest(
        ArchiveAddendumRequest request,
        ArchiveFile storedFile,
        string addendumHash,
        ArchiveSealContext seal)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(storedFile);
        ArgumentException.ThrowIfNullOrEmpty(addendumHash);
        ArgumentNullException.ThrowIfNull(seal);

        var manifest = BaseManifest("addendum", request.DocumentId, request.DocumentNumber, request.IssueDate, addendumHash, [storedFile], seal);
        manifest["addendumKind"] = request.Kind;
        return Serialize(manifest);
    }

    private static JsonObject BaseManifest(
        string entryKind,
        Guid documentId,
        string documentNumber,
        DateOnly issueDate,
        string packageHash,
        IReadOnlyList<ArchiveFile> files,
        ArchiveSealContext seal)
    {
        return new JsonObject
        {
            ["schemaVersion"] = "1",
            ["entryKind"] = entryKind,
            ["documentId"] = documentId.ToString(),
            ["documentNumber"] = documentNumber,
            ["issueDate"] = issueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["archivedUtc"] = seal.ArchivedUtc.ToString("O", CultureInfo.InvariantCulture),
            ["packageHash"] = packageHash,
            ["chainHash"] = seal.ChainHash,
            ["files"] = FilesNode(files),
            ["notice"] = Notice,
        };
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

    private static byte[] BuildArchiveMetadataBytes(
        Guid documentId,
        string documentNumber,
        DateOnly issueDate,
        string? mappingTraceJson,
        IReadOnlyList<ArchiveAbsentPiece> absentPieces)
    {
        var obj = new JsonObject
        {
            ["documentId"] = documentId.ToString(),
            ["documentNumber"] = documentNumber,
            ["issueDate"] = issueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["mappingTrace"] = ParseTraceOrNull(mappingTraceJson),
            ["absentPieces"] = AbsentPiecesNode(absentPieces),
        };
        return Encoding.UTF8.GetBytes(obj.ToJsonString(ManifestJsonOptions));
    }

    private static JsonArray AbsentPiecesNode(IReadOnlyList<ArchiveAbsentPiece> absentPieces)
    {
        var array = new JsonArray();
        foreach (ArchiveAbsentPiece piece in absentPieces)
        {
            array.Add(new JsonObject
            {
                ["piece"] = piece.Piece,
                ["reason"] = piece.Reason,
            });
        }

        return array;
    }

    private static JsonNode? ParseTraceOrNull(string? mappingTraceJson)
    {
        if (string.IsNullOrWhiteSpace(mappingTraceJson))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(mappingTraceJson);
        }
        catch (JsonException)
        {
            // Trace non-JSON : conservée telle quelle en texte plutôt que perdue (audit).
            return JsonValue.Create(mappingTraceJson);
        }
    }

    private static void AddOptionalPiece(
        List<ArchiveFile> files,
        List<ArchiveAbsentPiece> absent,
        string pieceKey,
        ArchiveAttachment? attachment,
        string? absenceReason)
    {
        if (attachment is not null)
        {
            files.Add(CreateFile(
                ArchivePackageLayout.SanitizeSegment(attachment.FileName),
                attachment.ContentType,
                attachment.Content));
            return;
        }

        // Absence : motif obligatoire (validé en amont par le service) — jamais silencieuse.
        absent.Add(new ArchiveAbsentPiece(pieceKey, absenceReason ?? string.Empty));
    }

    private static ArchiveFile CreateFile(string name, string contentType, byte[] content) =>
        new(name, contentType, content, Sha256Hex.OfBytes(content));

    private static byte[] Serialize(JsonObject manifest) =>
        Encoding.UTF8.GetBytes(manifest.ToJsonString(ManifestJsonOptions));
}
