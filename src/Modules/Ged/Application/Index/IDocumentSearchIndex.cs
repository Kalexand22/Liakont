namespace Liakont.Modules.Ged.Application.Index;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Port INTERNE de recherche &amp; d'index GED (F19 §6.1-§6.4, ADR-0035). C'est le seam de recherche (pas un axe
/// enfichable en V1 ; un backend OpenSearch/pgvector viendra DERRIÈRE ce contrat en fast-follow — GED21/GED22,
/// jamais un <c>if (index is Concret)</c>, règle 8). L'implémentation V1 s'appuie sur <c>tsvector</c> PostgreSQL et
/// la table dérivée reconstructible <c>ged_index.document_search</c> (foyer UNIQUE du plein-texte document, §6.3).
/// Tout est tenant-scopé par la connexion (F19 §3.2) ; le prédicat de confidentialité est MATÉRIALISÉ dans le SQL
/// (RL-31, anti-oracle) — recherche, facette et graphe ne révèlent aucun axe/entité confidentiel sans le droit.
/// </summary>
public interface IDocumentSearchIndex
{
    /// <summary>
    /// (Re)projette le <c>search_vector</c> d'UN document géré depuis l'index tenant (titre = poids A, valeurs des
    /// axes SEARCHABLES et NON CONFIDENTIELS = poids B ; §6.1/§6.3). Idempotent (UPSERT) et RECONSTRUCTIBLE : ne lit
    /// que la base tenant (jamais le staging), donc réutilisable tel quel par un rebuild total ou le backfill (GED10).
    /// Les axes confidentiels sont EXCLUS du vecteur partagé au build (INV-GED-10). No-op si le document est absent.
    /// </summary>
    Task RefreshDocumentAsync(Guid managedDocumentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recherche multi-axes (conjonction robuste aux axes multi-valeur, §6.2) + plein-texte (§6.3) + facettes, paginée
    /// CÔTÉ SQL en keyset (RL-20, jamais OFFSET/chargement-tout). La confidentialité est appliquée server-side :
    /// un critère/une facette sur un axe confidentiel sans <see cref="DocumentSearchQuery.HasConfidentialRight"/>
    /// ne remonte aucun résultat et aucun compte (anti-oracle, RL-31).
    /// </summary>
    Task<DocumentSearchResult> SearchAsync(DocumentSearchQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Explore le graphe d'entités depuis une racine et retourne les documents ATTEIGNABLES (§6.4). Traversée
    /// BIDIRECTIONNELLE et BORNÉE : borne de profondeur DURE (anti-DoS), anti-cycle, pagination keyset (INV-GED-09).
    /// Confidentialité héritée des <c>entity_types</c> aux extrémités ET à la racine (RL-31, fail-closed) : une racine
    /// confidentielle sans le droit renvoie un ensemble VIDE (pas d'oracle depth-0).
    /// </summary>
    Task<GraphExplorationResult> ExploreGraphAsync(GraphExplorationQuery query, CancellationToken cancellationToken = default);
}
