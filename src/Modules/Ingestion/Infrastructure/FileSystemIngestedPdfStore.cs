namespace Liakont.Modules.Ingestion.Infrastructure;

using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Liakont.Modules.Ingestion.Application;
using Microsoft.Extensions.Options;

/// <summary>
/// Stockage FICHIER des PDF reçus (PIV04), organisé par tenant sous une racine de déploiement (voir
/// <c>docs/adr/ADR-0008-stockage-pdf-ingestion.md</c>). Arborescence :
/// <c>{racine}/{tenant}/linked/{sha256(sourceReference)}.pdf</c> pour les PDF rattachés (adressables de
/// façon déterministe par leur référence source, re-push = écrasement idempotent) et
/// <c>{racine}/{tenant}/pool/{guid}__{nomFichier}</c> pour le pool de réconciliation (chaque dépôt
/// conservé distinctement, F06/TRK07). Aucune dépendance au module Document du socle (frontière PIV04).
/// </summary>
internal sealed class FileSystemIngestedPdfStore : IIngestedPdfStore
{
    private const string LinkedFolder = "linked";
    private const string PoolFolder = "pool";

    private readonly IngestionStorageOptions _options;

    public FileSystemIngestedPdfStore(IOptions<IngestionStorageOptions> options)
    {
        _options = options.Value;
    }

    public async Task<string> SaveLinkedPdfAsync(string tenantId, string sourceReference, Stream content, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceReference))
        {
            throw new ArgumentException("La référence source est obligatoire pour un PDF rattaché.", nameof(sourceReference));
        }

        var fileName = HashToFileName(sourceReference) + ".pdf";
        var relativePath = Path.Combine(SafeTenant(tenantId), LinkedFolder, fileName);
        await WriteAsync(relativePath, content, overwrite: true, cancellationToken);
        return relativePath;
    }

    public async Task<string> SavePooledPdfAsync(string tenantId, string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        var safeName = SafeFileName(fileName);
        var unique = Guid.NewGuid().ToString("N") + "__" + safeName;
        var relativePath = Path.Combine(SafeTenant(tenantId), PoolFolder, unique);
        await WriteAsync(relativePath, content, overwrite: false, cancellationToken);
        return relativePath;
    }

    /// <summary>Slug de tenant sécurisé pour un segment de chemin (anti path-traversal).</summary>
    private static string SafeTenant(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("Le tenant est obligatoire.", nameof(tenantId));
        }

        return Sanitize(tenantId.Trim());
    }

    private static string SafeFileName(string? fileName)
    {
        // On ne conserve que le nom de base (jamais un chemin) puis on l'assainit.
        var baseName = string.IsNullOrWhiteSpace(fileName) ? "document.pdf" : Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "document.pdf";
        }

        return Sanitize(baseName);
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
        }

        return builder.ToString();
    }

    private static string HashToFileName(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var hex = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return hex.ToString();
    }

    private async Task WriteAsync(string relativePath, Stream content, bool overwrite, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var root = _options.PdfRootPath;
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException(
                "Le chemin racine de stockage des PDF (Ingestion:Storage:PdfRootPath) n'est pas configuré.");
        }

        var fullPath = Path.Combine(root, relativePath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"Chemin de stockage PDF invalide : {fullPath}");
        Directory.CreateDirectory(directory);

        var mode = overwrite ? FileMode.Create : FileMode.CreateNew;
        await using var file = new FileStream(fullPath, mode, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(file, cancellationToken);
    }
}
