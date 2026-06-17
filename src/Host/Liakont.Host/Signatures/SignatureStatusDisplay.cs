namespace Liakont.Host.Signatures;

using Liakont.Modules.DocumentApproval.Contracts;
using Stratum.Common.UI.Models;

/// <summary>
/// Aides de PRÉSENTATION (libellés français + sévérité de badge) pour la page console des signatures (SIG10).
/// Pur affichage — AUCUNE règle métier : les libellés reflètent la documentation des énumérations
/// DocumentApproval/Signature (ADR-0027/0028, F17), ils n'inventent aucune sémantique fiscale (CLAUDE.md n°2/12).
/// Les états et niveaux sont reçus sous forme de NOM (chaîne) depuis les DTO <c>Contracts</c> (surface sans
/// dépendance sur le <c>Domain</c>) ; un nom inconnu retombe sur le nom brut plutôt que d'échouer.
/// </summary>
internal static class SignatureStatusDisplay
{
    /// <summary>Libellé opérateur (français) d'un état de validation (nom de <c>ValidationState</c>).</summary>
    public static string StateLabel(string state) => state switch
    {
        "PendingValidation" => "En attente de validation",
        "ValidationInProgress" => "Validation en cours",
        "Validated" => "Validé",
        "TacitlyValidated" => "Validé tacitement",
        "Rejected" => "Refusé",
        "Contested" => "Contesté",
        "Expired" => "Expiré",
        _ => state,
    };

    /// <summary>
    /// Sévérité du badge d'état (couleur) : non terminal = attention/info ; validé = succès ; refus = rouge ;
    /// expiration = neutre. Pure convention d'affichage, sans incidence sur la machine d'états.
    /// </summary>
    public static Severity StateSeverity(string state) => state switch
    {
        "PendingValidation" => Severity.Warning,
        "ValidationInProgress" => Severity.Info,
        "Validated" => Severity.Success,
        "TacitlyValidated" => Severity.Success,
        "Rejected" => Severity.Error,
        "Contested" => Severity.Error,
        "Expired" => Severity.Neutral,
        _ => Severity.Neutral,
    };

    /// <summary>Libellé opérateur (français) d'une finalité de validation (reflète la doc d'énum ADR-0028, sans invention).</summary>
    public static string PurposeLabel(ValidationPurpose purpose) => purpose switch
    {
        ValidationPurpose.SelfBilledAcceptance => "Acceptation d'auto-facture (389)",
        ValidationPurpose.MandateSignature => "Signature de mandat",
        ValidationPurpose.CreditNoteAcceptance => "Acceptation d'avoir auto-facturé (261)",
        ValidationPurpose.ProgressStatementApproval => "Approbation de relevé d'avancement",
        ValidationPurpose.MultiTierAccountingApproval => "Circuit comptable multi-paliers",
        ValidationPurpose.MultiPartySignature => "Signature multi-parties",
        _ => purpose.ToString(),
    };

    /// <summary>Libellé d'un niveau de preuve (nom de <c>SignatureLevel</c>) ; « Aucun » pour None.</summary>
    public static string ProofLevelLabel(string proofLevel) => proofLevel switch
    {
        "None" => "Aucun",
        "Recorded" => "Acceptation enregistrée",
        "SES" => "Signature simple (SES)",
        "AES" => "Signature avancée (AES)",
        "QES" => "Signature qualifiée (QES)",
        _ => proofLevel,
    };

    /// <summary>Libellé d'un état de slot d'approbation N-parties (nom de <c>ApprovalSlotState</c>).</summary>
    public static string SlotStateLabel(string slotState) => slotState switch
    {
        "Pending" => "En attente",
        "Approved" => "Approuvé",
        "Rejected" => "Refusé",
        _ => slotState,
    };
}
