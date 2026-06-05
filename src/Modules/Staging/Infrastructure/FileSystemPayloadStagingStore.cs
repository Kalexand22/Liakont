namespace Liakont.Modules.Staging.Infrastructure;

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Agent.Contracts.Serialization;
using Liakont.Modules.Staging.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

/// <summary>
/// Magasin de staging sur système de fichiers (store V1 par défaut, appliance self-hosted — ADR-0014 ;
/// même esprit que le coffre FileSystem). Un répertoire par tenant sous la racine d'instance, un fichier
/// chiffré par document. NON-WORM : l'écriture est ré-écrivable (idempotente) et l'entrée est PURGEABLE
/// (magasin transitoire de traitement, jamais audit/WORM — CLAUDE.md n°4 inchangé).
///
/// <b>Chiffré au repos</b> via ASP.NET Core Data Protection, avec un protecteur dérivé par tenant
/// (isolation cryptographique inter-tenants — CLAUDE.md n°9/10). <b>Intégrité à la lecture</b> : le payload
/// est re-haché (ADR-0007) et comparé à l'empreinte attendue ; un blob illisible (déchiffrement échoué,
/// AEAD) ou un contenu altéré est REJETÉ, jamais servi.
/// </summary>
public sealed class FileSystemPayloadStagingStore : IPayloadStagingStore
{
    private const string ProtectorPurpose = "Liakont.Staging.Payload.v1";

    private readonly string _rootPath;
    private readonly IDataProtectionProvider _dataProtectionProvider;

    /// <summary>Construit le magasin à partir de la racine configurée et du fournisseur de protection des données.</summary>
    /// <param name="options">Options du magasin (racine d'instance).</param>
    /// <param name="dataProtectionProvider">Fournisseur ASP.NET Core Data Protection (chiffrement au repos).</param>
    public FileSystemPayloadStagingStore(
        IOptions<FileSystemPayloadStagingStoreOptions> options,
        IDataProtectionProvider dataProtectionProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);

        string root = options.Value.RootPath;
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException(
                "Le magasin de staging FileSystem requiert une racine (Staging:Storage:FileSystem:RootPath).");
        }

        _rootPath = Path.GetFullPath(root);
        _dataProtectionProvider = dataProtectionProvider;
    }

    /// <inheritdoc />
    public PayloadStagingStoreCapabilities Capabilities => PayloadStagingStoreCapabilities.None;

    /// <inheritdoc />
    public async Task WriteAsync(StagedPayloadKey key, string canonicalJson, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrEmpty(canonicalJson);

        byte[] cipher = ProtectorFor(key.TenantId).Protect(Encoding.UTF8.GetBytes(canonicalJson));
        string fullPath = ResolveFullPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        string tempPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            // WriteThrough + Flush(flushToDisk) : le contenu est DURABLE sur le disque avant le retour.
            // C'est la garantie de l'invariant d'ordre de l'intake (le blob est flushé AVANT que l'événement
            // d'ingestion ne soit committé — ADR-0014 §2).
            await using (var stream = new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 4096, FileOptions.WriteThrough))
            {
                await stream.WriteAsync(cipher, cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            // Renommage atomique (même volume) : jamais de fichier partiel au chemin canonique. Idempotent
            // sur la clé — ré-écrire le même contenu logique remplace proprement (filet de sécurité au renvoi).
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
    public async Task<string> ReadAsync(StagedPayloadKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        string fullPath = ResolveFullPath(key);
        if (!File.Exists(fullPath))
        {
            // Absence = transitoire (« pas encore stagé / à re-tenter »), JAMAIS terminal/perdu (ADR-0014 §3).
            throw StagedPayloadNotFoundException.ForKey(key);
        }

        byte[] cipher = await File.ReadAllBytesAsync(fullPath, cancellationToken);

        string canonicalJson;
        try
        {
            byte[] plaintext = ProtectorFor(key.TenantId).Unprotect(cipher);
            canonicalJson = Encoding.UTF8.GetString(plaintext);
        }
        catch (CryptographicException ex)
        {
            // Blob altéré : l'authentification AEAD du chiffrement échoue → rejet d'intégrité.
            throw StagedPayloadIntegrityException.Undecryptable(key, ex);
        }

        string actualHash = PayloadHasher.ComputeHash(canonicalJson);
        if (!string.Equals(actualHash, key.PayloadHash, StringComparison.Ordinal))
        {
            throw StagedPayloadIntegrityException.HashMismatch(key, actualHash);
        }

        return canonicalJson;
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(StagedPayloadKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(ResolveFullPath(key)));
    }

    /// <inheritdoc />
    public Task PurgeAsync(StagedPayloadKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();

        string fullPath = ResolveFullPath(key);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    private IDataProtector ProtectorFor(string tenantId) =>
        _dataProtectionProvider.CreateProtector(ProtectorPurpose, tenantId);

    private string ResolveFullPath(StagedPayloadKey key)
    {
        string tenantSegment = StagingPathLayout.SanitizeSegment(key.TenantId);
        string tenantRoot = Path.GetFullPath(Path.Combine(_rootPath, tenantSegment));

        // ADRESSAGE PAR CONTENU (payload_hash), PAS par DocumentId (ADR-0014 §2). Une nouvelle tentative
        // après un crash regénère un DocumentId mais conserve le MÊME payload_hash (même contenu) → même
        // chemin → ré-écriture idempotente qui RÉCLAME le blob orphelin au renvoi de l'agent (sans quoi un
        // DocumentId neuf laisserait l'orphelin s'accumuler sans jamais le réutiliser). L'empreinte (64 hex)
        // est intrinsèquement sûre ; on l'assainit par défense et on vérifie le périmètre tenant.
        string fileName = StagingPathLayout.SanitizeSegment(key.PayloadHash) + StagingPathLayout.PayloadFileExtension;
        string fullPath = Path.GetFullPath(Path.Combine(tenantRoot, fileName));

        if (!fullPath.StartsWith(tenantRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("Chemin de staging hors du périmètre du tenant.", nameof(key));
        }

        return fullPath;
    }
}
