namespace Liakont.Modules.Supervision.Application;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Supervision.Domain;

/// <summary>
/// Persistance des alertes du tenant courant (la connexion EST le tenant — database-per-tenant,
/// blueprint §7). Surface INTERNE au module (le moteur et le service d'acquittement l'utilisent) ; la
/// surface publique de lecture est <c>IAlertQueries</c> (Contracts). L'alerte est de l'état opérationnel
/// MUTABLE (résolution, acquittement), distinct de la piste d'audit append-only.
/// </summary>
public interface IAlertStore
{
    /// <summary>L'alerte ACTIVE (non résolue) de la règle <paramref name="ruleKey"/>, ou <c>null</c> (anti-bruit).</summary>
    Task<Alert?> FindActiveByRuleAsync(string ruleKey, CancellationToken cancellationToken = default);

    /// <summary>Alerte par identifiant, ou <c>null</c> (utilisé par l'acquittement).</summary>
    Task<Alert?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Insère une nouvelle alerte. Garde-fou anti-bruit : si une alerte active de la même règle existe déjà
    /// (course), l'insertion est ignorée (idempotente) plutôt que de créer un doublon actif.
    /// </summary>
    Task InsertAsync(Alert alert, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persiste UNIQUEMENT la résolution d'une alerte existante (<c>resolved_utc</c>). N'écrit pas les
    /// colonnes d'acquittement : aucune perte de mise à jour concurrente entre résolution et acquittement.
    /// </summary>
    Task ResolveAsync(Alert alert, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persiste UNIQUEMENT l'acquittement d'une alerte existante (<c>acknowledged_by</c>,
    /// <c>acknowledged_utc</c>). N'écrit pas <c>resolved_utc</c> : aucune perte de mise à jour concurrente
    /// entre acquittement et résolution.
    /// </summary>
    Task AcknowledgeAsync(Alert alert, CancellationToken cancellationToken = default);
}
