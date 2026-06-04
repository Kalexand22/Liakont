namespace Liakont.Modules.Documents.Contracts.Deduplication;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Anti-doublon AVANT envoi (item TRK03, F06 §4), exposé par le module Documents au PIPELINE (consommé
/// par PIP01 : avant de transmettre un document à la Plateforme Agréée, le pipeline interroge ce port et
/// bloque ou autorise selon le verdict — F06 §4). La recherche est TENANT-SCOPÉE PAR CONSTRUCTION : elle
/// s'exécute sur la base DU TENANT courant (la connexion EST le tenant — database-per-tenant, blueprint
/// §7) ; aucune comparaison cross-tenant n'est possible (CLAUDE.md n°9/17).
/// </summary>
public interface IDuplicateDocumentCheck
{
    /// <summary>
    /// Évalue les quatre règles d'anti-doublon de F06 §4 pour le document candidat et retourne le verdict
    /// (envoi autorisé, renvoi après rejet, doublon émis, doublon strict par empreinte).
    /// </summary>
    Task<DuplicateCheckResult> EvaluateAsync(DuplicateCheckRequest request, CancellationToken cancellationToken = default);
}
