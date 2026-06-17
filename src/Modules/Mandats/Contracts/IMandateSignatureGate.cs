namespace Liakont.Modules.Mandats.Contracts;

/// <summary>
/// Port de purpose <c>MandateSignature</c> (signature du CONTRAT DE MANDAT — ADR-0022 §3, F17 §3.4 ; SIG06).
/// Exposé par <c>Mandats.Contracts</c> (le mandat est un concept du module Mandats) ; le consommateur n'accède
/// JAMAIS au module DocumentApproval concret (frontière inter-modules, CLAUDE.md n°6/14 ; chaîne NetArchTest
/// ADR-0028 §9). L'implémentation délègue à la Règle de gate générique (<c>IDocumentApprovalGate</c>) pour le
/// purpose <c>MandateSignature</c> ; le niveau eIDAS requis est un PARAMÉTRAGE TENANT (F17 §7 ; reco AES, jamais
/// une obligation produit — CLAUDE.md n°2/3). Toujours scopé par <paramref name="companyId"/> (CLAUDE.md n°9).
/// </summary>
public interface IMandateSignatureGate
{
    /// <summary>
    /// Statue sur l'ouverture du gate de signature du mandat <paramref name="documentId"/> (état × niveau de
    /// preuve requis du tenant), sur sa tentative la plus récente. Fail-closed si aucune validation n'existe.
    /// </summary>
    Task<DocumentGateDecision> EvaluateAsync(Guid companyId, Guid documentId, CancellationToken ct = default);
}
