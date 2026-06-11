namespace Liakont.Modules.Supervision.Domain;

/// <summary>
/// Gravité d'une alerte de supervision (F12 §5.2). Persistée par son NOM (texte), comme les autres
/// énumérations du produit — robuste à un renumérotage et lisible en base. L'UI (SUP02) mappe vers
/// les libellés visuels 🔴 Critique / 🟠 Avertissement.
/// </summary>
public enum AlertSeverity
{
    /// <summary>🟠 Avertissement — anomalie à traiter, sans urgence de conformité immédiate.</summary>
    Warning,

    /// <summary>🔴 Critique — risque de non-conformité (panne silencieuse, rejet PA, échéance) : action requise.</summary>
    Critical,
}
