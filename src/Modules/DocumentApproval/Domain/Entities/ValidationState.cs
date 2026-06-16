namespace Liakont.Modules.DocumentApproval.Domain.Entities;

/// <summary>
/// État du workflow de validation de document (ADR-0028 §3, F17 §3). Machine <b>FERMÉE</b> à <b>7 états
/// DISTINCTS</b> : <see cref="PendingValidation"/> est le seul état initial, avec des arêtes DIRECTES vers
/// les terminaux (chemin <c>Recorded</c>/synchrone) ET vers <see cref="ValidationInProgress"/> (intermédiaire
/// OPTIONNEL, purposes signature asynchrones). Aucun retour arrière depuis un terminal (INV-APPROVAL-2).
/// <para>
/// ⚠️ <see cref="Rejected"/> (refus des purposes signature) et <see cref="Contested"/> (refus du self-billing,
/// sens fiscal : avoir 261) sont <b>DEUX états séparés</b>, pas un renommage — aucun purpose n'utilise les
/// deux (ADR-0028 §3). Valeurs persistées (int) — l'ordre est figé : tout changement casse les données
/// existantes ET l'index unique partiel des non-terminaux (V002, qui code 0/1 en dur).
/// </para>
/// </summary>
public enum ValidationState
{
    /// <summary>État initial (non terminal) : émission bloquée tant qu'on n'a pas validé.</summary>
    PendingValidation = 0,

    /// <summary>Demande de signature EN COURS (non terminal ; purposes signature asynchrones uniquement). Intermédiaire OPTIONNEL.</summary>
    ValidationInProgress = 1,

    /// <summary>Validation EXPRESSE (Recorded, ou preuve SES/AES/QES rattachée) — terminal. N'ouvre PAS le gate inconditionnellement (Règle de gate §5).</summary>
    Validated = 2,

    /// <summary>Bascule TACITE par job au-delà de l'échéance, si la politique du purpose l'autorise — terminal. Éligible au gate sous réserve de la Règle de gate §5.</summary>
    TacitlyValidated = 3,

    /// <summary>Refus d'une demande de signature — terminal (purposes signature). Correction = document compensatoire, jamais retour arrière.</summary>
    Rejected = 4,

    /// <summary>Contestation d'un self-billing dans le délai — terminal (sens fiscal : avoir 261). Définitif.</summary>
    Contested = 5,

    /// <summary>Expiration (délai dépassé sans complétion) — terminal (purposes signature / timeout N-parties).</summary>
    Expired = 6,
}
