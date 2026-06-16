namespace Liakont.Modules.SupportTrace.Infrastructure;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.SupportTrace.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

/// <summary>
/// Store de trace de support sur système de fichiers (V1 par défaut, appliance self-hosted — même esprit que
/// le magasin de staging). Un répertoire par tenant sous la racine d'instance, partitionné par jour de
/// transmission (porte la rétention) ; un fichier chiffré par document. NON-WORM : l'écriture est
/// ré-écrivable (idempotente) et l'entrée est PURGEABLE (store transitoire de support, jamais audit/WORM —
/// CLAUDE.md n°4 inchangé). <b>Chiffré au repos</b> via ASP.NET Core Data Protection, protecteur dérivé par
/// tenant (isolation cryptographique inter-tenants — CLAUDE.md n°9/10). Le store n'a AUCUNE connaissance de
/// <c>documents.document_events</c> ni du coffre d'archive : la purge ne peut donc pas les altérer.
/// </summary>
public sealed class FileSystemSupportTraceStore : ISupportTraceStore
{
    private const string ProtectorPurpose = "Liakont.SupportTrace.FacturX.v1";

    private readonly string _rootPath;
    private readonly IDataProtectionProvider _dataProtectionProvider;

    /// <summary>Construit le store à partir de la racine configurée et du fournisseur de protection des données.</summary>
    /// <param name="options">Options du store (racine d'instance + rétention).</param>
    /// <param name="dataProtectionProvider">Fournisseur ASP.NET Core Data Protection (chiffrement au repos).</param>
    public FileSystemSupportTraceStore(
        IOptions<SupportTraceOptions> options,
        IDataProtectionProvider dataProtectionProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);

        string root = options.Value.RootPath;
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException(
                "Le store de trace de support FileSystem requiert une racine (SupportTrace:RootPath).");
        }

        _rootPath = Path.GetFullPath(root);
        _dataProtectionProvider = dataProtectionProvider;
    }

    /// <inheritdoc />
    public async Task WriteAsync(
        string tenantId,
        Guid documentId,
        ReadOnlyMemory<byte> facturX,
        DateTimeOffset recordedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (documentId == Guid.Empty)
        {
            throw new ArgumentException("L'identifiant de document d'une trace de support est obligatoire.", nameof(documentId));
        }

        if (facturX.IsEmpty)
        {
            throw new ArgumentException("L'artefact Factur-X d'une trace de support ne peut pas être vide.", nameof(facturX));
        }

        byte[] cipher = ProtectorFor(tenantId).Protect(facturX.ToArray());
        string fullPath = ResolveFilePath(tenantId, documentId, recordedAtUtc);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        string tempPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            // WriteThrough + Flush(flushToDisk) : durable avant le retour, puis renommage atomique (même
            // volume) — jamais de fichier partiel au chemin canonique, ré-écriture idempotente sur la clé.
            await using (var stream = new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 4096, FileOptions.WriteThrough))
            {
                await stream.WriteAsync(cipher, cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    /// <inheritdoc />
    public async Task<byte[]?> ReadAsync(string tenantId, Guid documentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (documentId == Guid.Empty)
        {
            throw new ArgumentException("L'identifiant de document d'une trace de support est obligatoire.", nameof(documentId));
        }

        string tenantRoot = ResolveTenantRoot(tenantId);
        if (!Directory.Exists(tenantRoot))
        {
            return null;
        }

        string fileName = FileName(documentId);

        // Trace de support la PLUS RÉCENTE : on parcourt les répertoires-jour du plus récent au plus ancien
        // et on retourne le premier fichier de ce document trouvé (rare : geste de support, scan borné).
        string? mostRecent = null;
        DateOnly mostRecentDay = default;
        foreach (string dayDir in Directory.EnumerateDirectories(tenantRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!SupportTracePathLayout.TryParseDayDirectory(Path.GetFileName(dayDir), out DateOnly day))
            {
                continue;
            }

            string candidate = Path.Combine(dayDir, fileName);
            if (File.Exists(candidate) && (mostRecent is null || day > mostRecentDay))
            {
                mostRecent = candidate;
                mostRecentDay = day;
            }
        }

        if (mostRecent is null)
        {
            return null;
        }

        byte[] cipher = await File.ReadAllBytesAsync(mostRecent, cancellationToken);
        return ProtectorFor(tenantId).Unprotect(cipher);
    }

    /// <inheritdoc />
    public Task<int> PurgeOlderThanAsync(string tenantId, DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        string tenantRoot = ResolveTenantRoot(tenantId);
        if (!Directory.Exists(tenantRoot))
        {
            return Task.FromResult(0);
        }

        // Borne au JOUR (UTC) : on ne supprime QUE les répertoires-jour STRICTEMENT antérieurs à la date de
        // coupure — une entrée du jour de coupure est conservée (au plus un jour de marge, jamais sur-purgé).
        DateOnly cutoffDay = DateOnly.FromDateTime(cutoffUtc.UtcDateTime);

        int purged = 0;
        foreach (string dayDir in Directory.EnumerateDirectories(tenantRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // On n'efface QUE ce qu'on a écrit : un répertoire au nom non conforme (yyyy-MM-dd) est ignoré.
            if (SupportTracePathLayout.TryParseDayDirectory(Path.GetFileName(dayDir), out DateOnly day)
                && day < cutoffDay)
            {
                Directory.Delete(dayDir, recursive: true);
                purged++;
            }
        }

        return Task.FromResult(purged);
    }

    private static string FileName(Guid documentId) =>
        SupportTracePathLayout.SanitizeSegment(documentId.ToString("N")) + SupportTracePathLayout.TraceFileExtension;

    private IDataProtector ProtectorFor(string tenantId) =>
        _dataProtectionProvider.CreateProtector(ProtectorPurpose, tenantId);

    private string ResolveTenantRoot(string tenantId)
    {
        string tenantSegment = SupportTracePathLayout.SanitizeSegment(tenantId);
        return Path.GetFullPath(Path.Combine(_rootPath, tenantSegment));
    }

    private string ResolveFilePath(string tenantId, Guid documentId, DateTimeOffset recordedAtUtc)
    {
        string tenantRoot = ResolveTenantRoot(tenantId);
        string dayDir = Path.Combine(tenantRoot, SupportTracePathLayout.DayDirectory(recordedAtUtc));
        string fullPath = Path.GetFullPath(Path.Combine(dayDir, FileName(documentId)));

        if (!fullPath.StartsWith(tenantRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("Chemin de trace de support hors du périmètre du tenant.", nameof(tenantId));
        }

        return fullPath;
    }
}
