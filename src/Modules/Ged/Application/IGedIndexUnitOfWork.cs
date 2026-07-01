namespace Liakont.Modules.Ged.Application;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Ged.Domain.Index;

/// <summary>
/// Unité de travail transactionnelle Dapper de l'INDEX GED (base DU TENANT, schéma <c>ged_index</c> — l'isolation
/// EST la connexion, F19 §3.2/§3.7). Toutes les écritures d'un même appel partagent UNE transaction.
/// <para>
/// GED04 a livré <see cref="AppendAxisLinkAsync"/> (liens d'axe append-only, V009). GED05b ajoute le chemin
/// d'INDEXATION du consommateur d'ingestion : garde de concurrence par document
/// (<see cref="BeginDocumentIndexingAsync"/>), UPSERT de l'entité-pivot (<see cref="UpsertManagedDocumentAsync"/>),
/// résolution idempotente d'entité (<see cref="ResolveOrCreateEntityAsync"/>) et lien document↔entité
/// (<see cref="AppendDocumentEntityLinkAsync"/>). GED24 ajoute <see cref="AppendRelationAsync"/> (relations
/// entité↔entité append-only, V014) — on ne déclare aucune surface sans schéma sous-jacent (pas de méthode morte).
/// </para>
/// </summary>
public interface IGedIndexUnitOfWork : IAsyncDisposable
{
    /// <summary>
    /// Ouvre l'indexation d'un document sous GARDE DE CONCURRENCE (RL-04) : épingle l'isolation READ COMMITTED,
    /// prend un <c>pg_advisory_xact_lock</c> sur la clé du document (tenu jusqu'au commit — deux livraisons
    /// simultanées du même événement sont sérialisées), puis rend le STATUT courant de <c>managed_documents</c>
    /// pour ce document (<see langword="null"/> si le document n'est pas encore indexé). L'appelant no-ope si le
    /// statut est déjà terminal (<c>indexed</c>/<c>deferred</c>) — un replay ne réécrit rien.
    /// </summary>
    Task<string?> BeginDocumentIndexingAsync(Guid managedDocumentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// UPSERT l'entité-pivot d'index (<c>INSERT ... ON CONFLICT (id) DO NOTHING</c>, idempotence RL-04). Insérée
    /// directement à son statut FINAL (aucune mutation ⇒ aucune écriture au <c>managed_document_change_log</c>).
    /// </summary>
    Task UpsertManagedDocumentAsync(ManagedDocument document, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ajoute un lien d'axe (append pur — jamais d'UPDATE, le trigger l'interdit). Pour un axe MONO-valeur
    /// (<paramref name="isSingleValued"/> = <see langword="true"/>), prend une garde de concurrence
    /// (<c>pg_advisory_xact_lock</c> sur la clé document+axe) DANS la transaction AVANT de superséder la valeur
    /// courante (<c>supersedes_id</c>), garantissant une unique valeur courante même sous écritures concurrentes
    /// (RL-02). Pour un axe MULTI, insère simplement (plusieurs valeurs courantes admises). Rend l'<c>id</c> de la
    /// ligne créée.
    /// </summary>
    Task<Guid> AppendAxisLinkAsync(DocumentAxisLink link, bool isSingleValued, CancellationToken cancellationToken = default);

    /// <summary>
    /// Résout une instance d'entité par sa clé d'identité (§4.4) ou la CRÉE si absente (registre polymorphe
    /// <c>entity_instances</c>). Quand <paramref name="identityValue"/> est non nul, une entité existante du même
    /// type portant la même identité est RÉUTILISÉE — déduplication <b>best-effort par lookup</b> (§4.4, réutilise
    /// <c>ix_ei_identity</c>, index NON unique par choix de GED03c) : sous concurrence multi-instance de documents
    /// DISTINCTS partageant une même identité, un doublon TRANSITOIRE reste possible (deux lookups simultanés ne
    /// trouvent rien puis insèrent) — il se résout par la fusion manuelle <c>canonical_id</c> (§4.4), JAMAIS par une
    /// fusion automatique (irréversible sous append-only). Sans clé d'identité, une nouvelle instance est créée par
    /// observation. Une création est tracée dans <c>entity_instance_change_log</c> (append-only, <c>entity_created</c>).
    /// Rend l'<c>id</c> de l'entité (existante ou nouvelle).
    /// </summary>
    Task<Guid> ResolveOrCreateEntityAsync(
        Guid entityTypeId,
        string? identityValue,
        string displayName,
        string source,
        CancellationToken cancellationToken = default);

    /// <summary>Ajoute un lien document↔entité (append pur, jamais d'UPDATE — le trigger l'interdit). Rend son <c>id</c>.</summary>
    Task<Guid> AppendDocumentEntityLinkAsync(DocumentEntityLink link, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appende une relation entité↔entité (append PUR dans <c>ged_index.entity_relations</c> — jamais d'UPDATE,
    /// le trigger l'interdit). Le graphe est MULTI-valeur (plusieurs genres entre deux entités) : simple INSERT,
    /// pas de garde mono-valeur. Consommé par GED24 pour matérialiser les relations dérivées
    /// (<c>inferred</c>/<c>inherited</c>). Rend l'<c>id</c> de la ligne créée.
    /// </summary>
    Task<Guid> AppendRelationAsync(EntityRelation relation, CancellationToken cancellationToken = default);

    /// <summary>Valide la transaction (les écritures deviennent visibles et les verrous consultatifs sont relâchés).</summary>
    Task CommitAsync(CancellationToken cancellationToken = default);
}
