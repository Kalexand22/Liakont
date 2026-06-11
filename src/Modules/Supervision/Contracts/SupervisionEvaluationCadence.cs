namespace Liakont.Modules.Supervision.Contracts;

/// <summary>
/// Cadence du dead-man's-switch de supervision (F12 §5.1 : évaluation toutes les 15 minutes). Exposée dans
/// les Contracts car elle est consommée HORS du module Supervision (témoin de vie côté Host, restitution du
/// dispositif) — la valeur de référence reste F12 §5.1. La planification système (cron) doit rester alignée :
/// un test de verrou (SupervisionCadenceLockTests) casse en cas de dérive.
/// </summary>
public static class SupervisionEvaluationCadence
{
    /// <summary>Intervalle d'évaluation attendu, en minutes (F12 §5.1).</summary>
    public const int IntervalMinutes = 15;
}
