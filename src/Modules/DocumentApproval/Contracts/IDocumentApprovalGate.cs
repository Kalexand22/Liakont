namespace Liakont.Modules.DocumentApproval.Contracts;

using Liakont.Modules.DocumentApproval.Contracts.DTOs;

/// <summary>
/// Port de LECTURE de la Règle de gate générique (ADR-0028 §5, INV-APPROVAL-4), exposé à la frontière
/// <c>Contracts</c> (module-rules §3, CLAUDE.md n°6/14) pour que les ports de purpose (SIG06 :
/// <c>ISelfBilledGate</c>, <c>ICreditNoteAcceptanceGate</c>, <c>IMandateSignatureGate</c>,
/// <c>IMultiPartySignatureGate</c>) statuent sur l'émissibilité d'un document SANS tirer la persistance ni la
/// machine de domaine. L'évaluation est PURE (aucune mutation) et scopée par <paramref name="companyId"/>
/// (CLAUDE.md n°9, résolu par l'appelant).
/// <para>
/// Le niveau de preuve requis pour le purpose est résolu en interne (paramétrage tenant
/// <see cref="IDocumentApprovalRequirements"/>, défaut <c>Recorded</c>) : il ne traverse PAS cette interface, ce
/// qui garde <c>Contracts</c> sans dépendance sur <c>Signature.Contracts</c> (les niveaux restent exposés par leur
/// nom dans les DTO). Le gate n'est NI durci NI affaibli au nom d'une obligation inexistante (CLAUDE.md n°2/3) :
/// le durcissement éventuel vient du seul CHOIX du tenant.
/// </para>
/// </summary>
public interface IDocumentApprovalGate
{
    /// <summary>
    /// Statue sur l'ouverture du gate d'émission pour le document <paramref name="documentId"/> et le
    /// <paramref name="purpose"/>, sur sa tentative la PLUS RÉCENTE (ADR-0028 §6). Renvoie un verdict FERMÉ
    /// (fail-closed, « bloquer plutôt qu'émettre faux » — CLAUDE.md n°3) si aucune tentative n'existe.
    /// </summary>
    Task<ApprovalGateResult> EvaluateAsync(
        Guid companyId,
        Guid documentId,
        ValidationPurpose purpose,
        CancellationToken ct = default);
}
