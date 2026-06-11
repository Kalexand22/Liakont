namespace Liakont.Host.Supervision;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Fournit le témoin de vie du dead-man's-switch de supervision (FIX210, F12 §5.1) à partir des exécutions
/// du job SYSTÈME d'évaluation. Isole le composant bandeau de l'accès au module Job (le bandeau reste
/// présentationnel — CLAUDE.md n°19) et rend la lecture testable.
/// </summary>
public interface ISupervisionLivenessProvider
{
    /// <summary>Calcule l'état de vie courant du dispositif de supervision de l'instance.</summary>
    Task<SupervisionLivenessView> GetAsync(CancellationToken cancellationToken = default);
}
