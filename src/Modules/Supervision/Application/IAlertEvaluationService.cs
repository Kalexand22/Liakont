namespace Liakont.Modules.Supervision.Application;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Moteur de supervision (SUP01a) : évalue toutes les <see cref="IAlertRule"/> enregistrées pour UN tenant
/// et applique la mécanique anti-bruit / auto-résolution (déclencher une alerte si la condition apparaît,
/// la résoudre si elle disparaît, ne jamais la re-déclencher tant qu'elle est active). Appelé une fois par
/// tenant par le dead-man's-switch (job système qui fait le fan-out via <c>ITenantJobRunner</c>, SOL06).
/// </summary>
public interface IAlertEvaluationService
{
    /// <summary>Évalue toutes les règles pour <paramref name="tenantId"/> et retourne le bilan du cycle.</summary>
    Task<AlertEvaluationResult> EvaluateAsync(string tenantId, CancellationToken cancellationToken = default);
}
