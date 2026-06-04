namespace Liakont.Modules.Validation.Contracts;

/// <summary>
/// Sévérité d'une anomalie détectée par une règle de validation (F04 §5).
/// </summary>
public enum ValidationSeverity
{
    /// <summary>
    /// Anomalie BLOQUANTE : le document ne doit JAMAIS être envoyé tant qu'elle subsiste
    /// (F04 §1, CLAUDE.md n°3). Valeur par défaut volontaire (« bloquer plutôt qu'envoyer faux »).
    /// </summary>
    Blocking = 0,

    /// <summary>
    /// ALERTE : l'envoi reste possible mais l'opérateur est averti (ex. écart de totaux source —
    /// F04 §3.3).
    /// </summary>
    Warning = 1,
}
