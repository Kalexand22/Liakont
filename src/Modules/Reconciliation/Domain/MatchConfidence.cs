namespace Liakont.Modules.Reconciliation.Domain;

/// <summary>
/// Niveau de confiance d'un rapprochement PDF ↔ document (item TRK07, décision 2026-06-02).
/// La confiance pilote le comportement : <see cref="High"/> autorise le lien AUTOMATIQUE,
/// <see cref="Medium"/> impose une confirmation opérateur (jamais de lien automatique en dessous de la
/// confiance haute — un rapprochement erroné archivé en WORM serait incorrigible).
/// </summary>
public enum MatchConfidence
{
    /// <summary>Numéro de document trouvé dans le nom de fichier ou le texte du PDF → lien automatique.</summary>
    High,

    /// <summary>Correspondance date + montant TTC (candidat unique) → proposition, confirmation requise.</summary>
    Medium,
}
