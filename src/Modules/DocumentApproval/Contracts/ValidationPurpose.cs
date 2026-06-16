namespace Liakont.Modules.DocumentApproval.Contracts;

/// <summary>
/// Finalité de validation d'un document (ADR-0028 §4). Clé de couplage PUBLIQUE : un module exposeur
/// demande la validation d'un document POUR un purpose donné, et chaque purpose déclare son
/// <b>sous-graphe autorisé</b> (machine fermée projetée — Domain <c>ValidationPurposePolicy</c>). Le graphe
/// complet (7 états) est l'<b>union</b> des purposes ; aucun purpose n'accède à toutes les arêtes.
/// <para>
/// Valeurs persistées (int) — l'ordre est figé : tout changement casse les données existantes.
/// </para>
/// </summary>
public enum ValidationPurpose
{
    /// <summary>Acceptation d'une auto-facture 389 (ADR-0024). Sous-graphe 4 états, ré-essai EXCLU (Contested définitif).</summary>
    SelfBilledAcceptance = 0,

    /// <summary>Signature du contrat de mandat (ADR-0022). Signature expresse (pas de bascule tacite), ré-essai autorisé.</summary>
    MandateSignature = 1,

    /// <summary>Acceptation d'un avoir auto-facturé 261. Défaut défendable (ADR-0028 §Points NON TRANCHÉS #9) : même discipline que le 389.</summary>
    CreditNoteAcceptance = 2,

    /// <summary>Approbation d'un relevé d'avancement de chantier. Bascule tacite selon contrat.</summary>
    ProgressStatementApproval = 3,

    /// <summary>Circuit comptable multi-paliers (slots). Bascule tacite selon politique.</summary>
    MultiTierAccountingApproval = 4,

    /// <summary>Document co-signé N parties (slots). Timeout par <c>TenantJobRunner</c>.</summary>
    MultiPartySignature = 5,
}
