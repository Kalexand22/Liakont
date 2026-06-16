namespace Liakont.Modules.Mandats.Domain.Entities;

/// <summary>
/// Nature d'une modification journalisée du registre des mandants et du cycle de vie des mandats
/// (ADR-0022 §3, INV-MANDATS-3). Chaque mutation produit une entrée immuable de
/// <c>mandat_change_log</c> (append-only) qui en porte le type, l'auteur et la valeur avant/après —
/// la piste permet de prouver, des années après, qui a changé quoi (preuve fiscale croisée
/// mandant/mandataire, F15 §2.1).
/// </summary>
public enum MandatChangeType
{
    /// <summary>Création d'un mandant (tiers récurrent du tenant, F15 §2.2).</summary>
    CreateMandant = 0,

    /// <summary>Modification des coordonnées d'un mandant (raison sociale, n° TVA, SIREN, préfixe).</summary>
    UpdateMandant = 1,

    /// <summary>Création d'un mandat (clause, caractère écrit/tacite, F15 §1.5/§2.2).</summary>
    CreateMandat = 2,

    /// <summary>Modification d'un mandat (clause, statut d'assujettissement, délai de contestation) — repasse le mandat « non validé » (Invalidate).</summary>
    UpdateMandat = 3,

    /// <summary>Validation humaine d'un mandat (ADR-0022 §3, gabarit TvaMapping).</summary>
    ValidateMandat = 4,

    /// <summary>Révocation d'un mandat (l'autofacturation 389 est suspendue à compter de la révocation).</summary>
    RevokeMandat = 5,
}
