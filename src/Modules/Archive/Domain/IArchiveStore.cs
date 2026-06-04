namespace Liakont.Modules.Archive.Domain;

/// <summary>
/// Abstraction du coffre de stockage du module Archive — 3ᵉ axe de généricité enfichable (blueprint §2
/// règle 6, §6 ; module-rules §5 ; F12 §7 décision D9). Le module ne référence JAMAIS un backend concret
/// (<c>if (store is S3)</c> interdit, P1 — CLAUDE.md n°14) : il ne voit que cette abstraction et ses
/// <see cref="ArchiveStoreCapabilities"/> déclarées. Le choix du backend est une configuration
/// d'INSTANCE (FileSystem par défaut, S3-compatible en option ; Azure/GCS en fast-follow).
///
/// WORM : l'écriture est write-once. Il n'existe AUCUNE méthode de modification ou de suppression d'un
/// objet existant — l'immuabilité est dans la forme même de l'interface (CLAUDE.md n°4). L'intégrité
/// produit (chaîne de hashes + addenda chaînés) ne dépend JAMAIS du verrou natif du backend.
/// </summary>
public interface IArchiveStore
{
    /// <summary>Capacités natives déclarées du backend (verrou objet, legal hold).</summary>
    ArchiveStoreCapabilities Capabilities { get; }

    /// <summary>
    /// Écrit un objet de façon write-once sous (<paramref name="tenant"/>, <paramref name="relativePath"/>).
    /// Idempotent pour un contenu IDENTIQUE (ré-écriture sûre après reprise d'une transmission) ; lève
    /// <see cref="ArchiveWriteConflictException"/> si un contenu DIFFÉRENT existe déjà à ce chemin
    /// (tentative de réécriture = violation WORM). Applique le verrou natif du backend quand la capacité
    /// <see cref="ArchiveStoreCapabilities.SupportsObjectLock"/> est déclarée, EN PLUS de l'intégrité produit.
    /// </summary>
    Task WriteAsync(string tenant, string relativePath, byte[] content, CancellationToken cancellationToken = default);

    /// <summary>Indique si un objet existe à (<paramref name="tenant"/>, <paramref name="relativePath"/>).</summary>
    Task<bool> ExistsAsync(string tenant, string relativePath, CancellationToken cancellationToken = default);

    /// <summary>Lit le contenu d'un objet existant (vérification d'intégrité, export contrôle fiscal).</summary>
    Task<byte[]> ReadAsync(string tenant, string relativePath, CancellationToken cancellationToken = default);
}
