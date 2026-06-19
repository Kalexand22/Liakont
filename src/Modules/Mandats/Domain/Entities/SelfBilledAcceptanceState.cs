namespace Liakont.Modules.Mandats.Domain.Entities;

/// <summary>
/// État du workflow d'acceptation d'une auto-facture sous mandat (type 389, ADR-0024 §2, F15 §2.3).
/// La machine est <b>fermée</b> : <see cref="PendingAcceptance"/> est le seul état initial, et il ne peut
/// transiter que vers l'un des trois états terminaux ; aucun retour arrière depuis un état terminal
/// (INV-ACCEPT-4). L'acceptation est <b>orthogonale</b> à l'émission : elle ne touche jamais la machine
/// <c>DocumentState</c> du module <c>Documents</c> (INV-ACCEPT-1).
/// <para>
/// Valeurs persistées (int) — l'ordre est figé : tout changement casse les données existantes.
/// </para>
/// </summary>
public enum SelfBilledAcceptanceState
{
    /// <summary>État initial à la création d'un document self-billed : émission bloquée tant qu'on n'a pas accepté.</summary>
    PendingAcceptance = 0,

    /// <summary>Acceptation <b>expresse</b> (opérateur/mandant) — ouvre le gate d'émission (ADR-0024 §2).</summary>
    Accepted = 1,

    /// <summary>Acceptation <b>tacite</b> par non-contestation au-delà du délai (job MND04, mandat écrit uniquement) — ouvre le gate.</summary>
    TacitlyAccepted = 2,

    /// <summary>Contestation enregistrée dans le délai : la correction se fait par avoir + nouvelle facture (F15 §2.3), jamais un retour arrière. Ferme le gate.</summary>
    Contested = 3,
}
