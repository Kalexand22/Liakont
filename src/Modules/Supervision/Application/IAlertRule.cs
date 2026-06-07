namespace Liakont.Modules.Supervision.Application;

using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Supervision.Domain;

/// <summary>
/// Point d'extension du moteur de supervision (SUP01a) : UNE règle d'alerte (F12 §5.2). Les 8 règles
/// concrètes sont livrées par SUP01b et se branchent ici. Une règle est PURE vis-à-vis de l'alerte : elle
/// indique seulement si sa condition est ACTUELLEMENT remplie pour le tenant — le déclenchement, l'anti-bruit
/// et l'auto-résolution sont la responsabilité du moteur (<see cref="IAlertEvaluationService"/>).
/// </summary>
public interface IAlertRule
{
    /// <summary>Clé stable de la règle (ex. <c>agent.mute</c>) — identité de l'alerte côté anti-bruit.</summary>
    string RuleKey { get; }

    /// <summary>Gravité produite par cette règle quand elle se déclenche (F12 §5.2).</summary>
    AlertSeverity Severity { get; }

    /// <summary>
    /// Évalue la condition pour le tenant porté par <paramref name="context"/>. Retourne
    /// <see cref="AlertEvaluation.Firing"/> si la condition est remplie, sinon <see cref="AlertEvaluation.Clear"/>.
    /// </summary>
    Task<AlertEvaluation> EvaluateAsync(AlertEvaluationContext context, CancellationToken cancellationToken = default);
}
