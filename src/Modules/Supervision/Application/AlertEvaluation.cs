namespace Liakont.Modules.Supervision.Application;

/// <summary>
/// Verdict d'une règle pour un cycle d'évaluation : sa condition est-elle ACTUELLEMENT remplie, et un
/// message opérateur optionnel. Pas d'état d'alerte ici (déclenchement / résolution = moteur).
/// </summary>
public sealed class AlertEvaluation
{
    private AlertEvaluation(bool isFiring, string? detail)
    {
        IsFiring = isFiring;
        Detail = detail;
    }

    /// <summary>Vrai si la condition de la règle est remplie à cet instant.</summary>
    public bool IsFiring { get; }

    /// <summary>Message opérateur actionnable (français), ou <c>null</c>.</summary>
    public string? Detail { get; }

    /// <summary>La condition est remplie — l'alerte doit être active.</summary>
    public static AlertEvaluation Firing(string? detail = null) => new(true, detail);

    /// <summary>La condition n'est pas (ou plus) remplie — toute alerte active de cette règle doit se résoudre.</summary>
    public static AlertEvaluation Clear() => new(false, null);
}
