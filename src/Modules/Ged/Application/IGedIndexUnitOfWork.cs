namespace Liakont.Modules.Ged.Application;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Ged.Domain.Index;

/// <summary>
/// Unité de travail transactionnelle Dapper de l'INDEX GED (base DU TENANT, schéma <c>ged_index</c> — l'isolation
/// EST la connexion, F19 §3.2/§3.7). Toutes les écritures d'un même appel partagent UNE transaction.
/// <para>
/// GED04 livre <see cref="AppendAxisLinkAsync"/> (liens d'axe append-only, V009) ; GED24 ajoute
/// <see cref="AppendRelationAsync"/> (relations entité↔entité append-only, V014). Les méthodes
/// <c>UpsertManagedDocumentAsync</c> / <c>AppendEntityLinkAsync</c> de F19 §3.7 arriveront avec les items qui
/// posent/consomment leurs tables (<c>managed_documents</c> upsert = GED05b ; <c>document_entity_links</c> à
/// venir) — on ne déclare pas ici une surface sans schéma sous-jacent (pas de méthode morte).
/// </para>
/// </summary>
public interface IGedIndexUnitOfWork : IAsyncDisposable
{
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
    /// Appende une relation entité↔entité (append PUR dans <c>ged_index.entity_relations</c> — jamais d'UPDATE,
    /// le trigger l'interdit). Le graphe est MULTI-valeur (plusieurs genres entre deux entités) : simple INSERT,
    /// pas de garde mono-valeur. Consommé par GED24 pour matérialiser les relations dérivées
    /// (<c>inferred</c>/<c>inherited</c>). Rend l'<c>id</c> de la ligne créée.
    /// </summary>
    Task<Guid> AppendRelationAsync(EntityRelation relation, CancellationToken cancellationToken = default);

    /// <summary>Valide la transaction (les écritures deviennent visibles et les verrous consultatifs sont relâchés).</summary>
    Task CommitAsync(CancellationToken cancellationToken = default);
}
