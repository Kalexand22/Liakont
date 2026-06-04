namespace Liakont.Modules.Ingestion.Infrastructure;

using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Liakont.Modules.Ingestion.Application;
using Liakont.Modules.Ingestion.Contracts;
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

    public Task<IReadOnlyList<PooledPdfReference>> ListPooledPdfsAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var poolDirectory = ResolvePoolDirectory(tenantId);
        if (!Directory.Exists(poolDirectory))
        {
            // Pool inexistant (l'adaptateur ne pousse pas de PDF non liés, ou aucun dépôt encore) : vide,
            // jamais d'exception (la réconciliation n'a simplement rien à rapprocher).
            return Task.FromResult<IReadOnlyList<PooledPdfReference>>([]);
        }

        var references = new List<PooledPdfReference>();
        foreach (var path in Directory.EnumerateFiles(poolDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var poolPdfId = Path.GetFileName(path);
            references.Add(new PooledPdfReference(poolPdfId, DisplayName(poolPdfId)));
        }

        return Task.FromResult<IReadOnlyList<PooledPdfReference>>(references);
    }

    public Task<Stream> OpenPooledPdfAsync(string tenantId, string poolPdfId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(poolPdfId))
        {
            throw new ArgumentException("L'identifiant du PDF du pool est obligatoire.", nameof(poolPdfId));
        }

        // Anti path-traversal : l'identifiant doit être un nom de fichier nu (jamais un chemin), et le
        // chemin résolu doit rester strictement sous le répertoire du pool du tenant.
        if (Path.GetFileName(poolPdfId) != poolPdfId)
        {
            throw new ArgumentException("Identifiant de PDF du pool invalide (un nom de fichier est attendu).", nameof(poolPdfId));
        }

        var poolDirectory = ResolvePoolDirectory(tenantId);
        var poolWithSeparator = poolDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? poolDirectory
            : poolDirectory + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(poolDirectory, poolPdfId));
        if (!fullPath.StartsWith(poolWithSeparator, StringComparison.Ordinal) || !File.Exists(fullPath))
        {
            throw new FileNotFoundException($"PDF du pool introuvable pour ce tenant : {poolPdfId}.");
        }

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    /// <summary>Slug de tenant sécurisé pour un segment de chemin (anti path-traversal).</summary>
    private static string SafeTenant(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("Le tenant est obligatoire.", nameof(tenantId));
        }

        var safe = Sanitize(tenantId.Trim());

        // Un segment composé uniquement de points (« . », « .. »…) remonterait hors de la racine via
        // Path.Combine : on le refuse (le slug vient de l'identité authentifiée, mais le store ne
        // présume jamais la sûreté de son entrée). Défense complétée par le contrôle « sous la racine »
        // de WriteAsync.
        if (safe.Trim('.').Length == 0)
        {
            throw new ArgumentException("Slug de tenant invalide pour un chemin de stockage.", nameof(tenantId));
        }

        return safe;
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

    /// <summary>Nom lisible d'un dépôt du pool : la partie après le préfixe d'unicité « {guid}__ ».</summary>
    private static string DisplayName(string poolPdfId)
    {
        var separator = poolPdfId.IndexOf("__", StringComparison.Ordinal);
        return separator >= 0 && separator + 2 < poolPdfId.Length
            ? poolPdfId[(separator + 2)..]
            : poolPdfId;
    }

    /// <summary>Chemin complet (validé sous la racine) du répertoire de pool d'un tenant.</summary>
    private string ResolvePoolDirectory(string tenantId)
    {
        var root = _options.PdfRootPath;
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException(
                "Le chemin racine de stockage des PDF (Ingestion:Storage:PdfRootPath) n'est pas configuré.");
        }

        var rootFull = Path.GetFullPath(root);
        var rootWithSeparator = rootFull.EndsWith(Path.DirectorySeparatorChar)
            ? rootFull
            : rootFull + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(rootFull, SafeTenant(tenantId), PoolFolder));
        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Chemin de pool PDF résolu hors de la racine de stockage.");
        }

        return fullPath;
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

        // Contrôle « sous la racine » (défense en profondeur, anti path-traversal) : le chemin résolu
        // doit rester strictement sous la racine de stockage, quel que soit le contenu des segments.
        var rootFull = Path.GetFullPath(root);
        var rootWithSeparator = rootFull.EndsWith(Path.DirectorySeparatorChar)
            ? rootFull
            : rootFull + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(rootFull, relativePath));
        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Chemin de stockage PDF résolu hors de la racine de stockage.");
        }

        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"Chemin de stockage PDF invalide : {fullPath}");
        Directory.CreateDirectory(directory);

        var mode = overwrite ? FileMode.Create : FileMode.CreateNew;
        await using var file = new FileStream(fullPath, mode, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(file, cancellationToken);
    }
}
