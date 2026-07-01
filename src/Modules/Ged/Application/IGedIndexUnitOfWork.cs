namespace Liakont.Modules.Ged.Application;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Ged.Domain.Index;

/// <summary>
/// Unité de travail transactionnelle Dapper de l'INDEX GED (base DU TENANT, schéma <c>ged_index</c> — l'isolation
/// EST la connexion, F19 §3.2/§3.7). Toutes les écritures d'un même appel partagent UNE transaction.
/// <para>
/// GED04 livre le seul chemin dont le schéma existe aujourd'hui : <see cref="AppendAxisLinkAsync"/> (liens d'axe
/// append-only, V009). Les méthodes <c>UpsertManagedDocumentAsync</c> / <c>AppendEntityLinkAsync</c> /
/// <c>AppendRelationAsync</c> de F19 §3.7 arriveront avec les items qui posent leurs tables
/// (<c>managed_documents</c> upsert = GED05b ; <c>entity_relations</c> / <c>document_entity_links</c> à venir) —
/// on ne déclare pas ici une surface sans schéma sous-jacent (pas de méthode morte).
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

    /// <summary>Valide la transaction (les écritures deviennent visibles et les verrous consultatifs sont relâchés).</summary>
    Task CommitAsync(CancellationToken cancellationToken = default);
}
