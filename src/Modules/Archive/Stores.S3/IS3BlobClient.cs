namespace Liakont.Modules.Archive.Stores.S3;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Couture minimale au-dessus du SDK S3 : les seules opérations objet dont le coffre a besoin. Elle isole
/// la dépendance AWSSDK (implémentation <see cref="AwsS3BlobClient"/>) et rend <see cref="S3ArchiveStore"/>
/// entièrement testable sans backend réel (double en mémoire). Le tour réel sur un S3-compatible
/// (Amazon/MinIO/OVH/Scaleway) est un test de staging, hors CI (blueprint §9).
/// </summary>
public interface IS3BlobClient
{
    /// <summary>
    /// Écrit un objet UNIQUEMENT s'il n'existe pas encore (création ATOMIQUE — <c>If-None-Match: *</c>).
    /// Retourne <c>true</c> si l'objet a été créé, <c>false</c> si un objet existait déjà à la clé
    /// (précondition échouée). C'est ce qui garantit le write-once même sous écriture concurrente : deux
    /// écrivains ne peuvent pas tous deux « créer » la même clé (pas de fenêtre de course entre un test
    /// d'existence et l'écriture). <paramref name="applyObjectLock"/> active le verrou objet natif quand
    /// la capacité est déclarée.
    /// </summary>
    Task<bool> TryPutIfAbsentAsync(string key, byte[] content, bool applyObjectLock, CancellationToken cancellationToken);

    /// <summary>Indique si un objet existe à la clé donnée.</summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken);

    /// <summary>Lit un objet, ou retourne <c>null</c> s'il est absent.</summary>
    Task<byte[]?> TryGetAsync(string key, CancellationToken cancellationToken);
}
