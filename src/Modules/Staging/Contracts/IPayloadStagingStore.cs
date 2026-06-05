namespace Liakont.Modules.Staging.Contracts;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Magasin de staging du contenu pivot à l'intake (ADR-0014) — abstraction à capacités enfichable
/// (V1 = FileSystem ; S3-compatible en fast-follow, comme le coffre). NON-WORM / <b>purgeable</b> :
/// magasin TRANSITOIRE de traitement, capacité DISTINCTE du coffre WORM <c>IArchiveStore</c> (qui n'expose
/// aucune suppression). Stocke le pivot sérialisé en forme canonique (ADR-0007), <b>chiffré au repos</b> et
/// <b>tenant-scopé</b> (CLAUDE.md n°9/10). Accédé via cette surface Contracts par <c>Ingestion</c> (write)
/// et le pipeline (read) — aucun accès cross-module Domain/Infrastructure (blueprint §6 ; CLAUDE.md n°14).
/// Aucun <c>if (store is …)</c> : le comportement est piloté par les <see cref="Capabilities"/> déclarées.
/// </summary>
public interface IPayloadStagingStore
{
    /// <summary>Capacités natives déclarées du backend (chiffrement serveur, expiration native).</summary>
    PayloadStagingStoreCapabilities Capabilities { get; }

    /// <summary>
    /// Écrit ET flushe durablement le JSON canonique du pivot (ADR-0007) sous la clé, chiffré au repos et
    /// tenant-scopé. <b>Idempotent</b> sur la clé : ré-écrire le même contenu est un no-op logique (filet de
    /// sécurité au renvoi de l'agent — ADR-0014 §2). L'écriture est durable AVANT le retour (invariant
    /// d'ordre de l'intake : le blob est flushé avant que l'événement d'ingestion ne soit committé).
    /// </summary>
    /// <param name="key">La clé tenant-scopée de l'entrée.</param>
    /// <param name="canonicalJson">Le pivot sérialisé en forme canonique ADR-0007 (montants decimal préservés).</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task WriteAsync(StagedPayloadKey key, string canonicalJson, CancellationToken cancellationToken = default);

    /// <summary>
    /// Relit le JSON canonique stagé et <b>re-vérifie le payload_hash</b> (ADR-0014 §3). Lève
    /// <see cref="StagedPayloadNotFoundException"/> si l'entrée est absente (transitoire / à re-tenter,
    /// jamais terminal) et <see cref="StagedPayloadIntegrityException"/> si le contenu relu ne correspond
    /// pas à <see cref="StagedPayloadKey.PayloadHash"/> (ou si le blob chiffré est illisible).
    /// </summary>
    /// <param name="key">La clé tenant-scopée de l'entrée.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <returns>Le JSON canonique du pivot, intègre.</returns>
    Task<string> ReadAsync(StagedPayloadKey key, CancellationToken cancellationToken = default);

    /// <summary>Indique si une entrée existe pour la clé (sans vérification d'intégrité ni déchiffrement).</summary>
    /// <param name="key">La clé tenant-scopée de l'entrée.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task<bool> ExistsAsync(StagedPayloadKey key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Purge (supprime) l'entrée de staging. Purge LÉGITIME : magasin transitoire de traitement, NI table
    /// d'audit NI coffre WORM (CLAUDE.md n°4/12 inchangés). Idempotent (no-op si déjà absente). La purge
    /// après émission DOIT être subordonnée à la présence du paquet WORM via
    /// <see cref="IStagingPurgeService"/> — ne JAMAIS purger sur la seule étiquette d'état (ADR-0014 §4).
    /// </summary>
    /// <param name="key">La clé tenant-scopée de l'entrée à purger.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task PurgeAsync(StagedPayloadKey key, CancellationToken cancellationToken = default);
}
