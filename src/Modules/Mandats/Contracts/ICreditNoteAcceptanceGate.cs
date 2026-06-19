namespace Liakont.Modules.Mandats.Contracts;

/// <summary>
/// Port de purpose <c>CreditNoteAcceptance</c> (acceptation de l'AVOIR AUTO-FACTURÉ 261 — F15 §6.5, F17 §3.4,
/// ADR-0028 §4 ; SIG06). Exposé par <c>Mandats.Contracts</c>, destiné à être consommé par <c>Pipeline</c> (jamais
/// le module DocumentApproval concret — chaîne NetArchTest ADR-0028 §9). L'implémentation délègue à la Règle de
/// gate générique (<c>IDocumentApprovalGate</c>) pour le purpose <c>CreditNoteAcceptance</c>.
/// <para>
/// <b>Existence du purpose (❓ #9, F15 §6.5 — défaut défendable PRIS).</b> ADR-0028 §9 / F17 §10 #9 tranchent un
/// défaut conservateur : le 261 est self-billed → il RÉ-ENTRE dans la MÊME discipline d'acceptation que le 389
/// (aucune valeur fiscale inventée, CLAUDE.md n°2). Le purpose existe donc et applique la même Règle de gate ; la
/// modalité fine reste un paramétrage que le client confirme avec SON EC au déploiement (jamais un gate produit).
/// </para>
/// Toujours scopé par <paramref name="companyId"/> (CLAUDE.md n°9). Fail-closed (« bloquer plutôt qu'émettre
/// faux », CLAUDE.md n°3).
/// </summary>
public interface ICreditNoteAcceptanceGate
{
    /// <summary>
    /// Statue sur l'ouverture du gate d'acceptation de l'avoir 261 <paramref name="documentId"/> (état × niveau
    /// de preuve requis du tenant), sur sa tentative la plus récente. Fail-closed si aucune validation n'existe.
    /// </summary>
    Task<DocumentGateDecision> EvaluateAsync(Guid companyId, Guid documentId, CancellationToken ct = default);
}
