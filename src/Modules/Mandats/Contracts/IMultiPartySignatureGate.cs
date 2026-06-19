namespace Liakont.Modules.Mandats.Contracts;

/// <summary>
/// Port de purpose <c>MultiPartySignature</c> (document CO-SIGNÉ N parties — F17 §3.4, ADR-0028 §8 ; SIG06). La
/// complétude est évaluée PAR SLOT (chaque signataire ≥ niveau requis du tenant ; un slot sous-niveau n'ouvre pas
/// le gate — ADR-0028 §5 cond. 2). L'implémentation délègue à la Règle de gate générique
/// (<c>IDocumentApprovalGate</c>) pour le purpose <c>MultiPartySignature</c>.
/// <para>
/// <b>Module exposeur (défaut défendable).</b> ADR-0028 §4 ne nomme PAS de module exposeur pour ce port (F17 §3.4 :
/// « à nommer en ADR-0028 »). Faute d'un module métier dédié à la co-signature au stade build, le port est
/// co-localisé dans <c>Mandats.Contracts</c> avec les autres ports de gate (frontière unique
/// consommateur → <c>Mandats.Contracts</c>, jamais le DocumentApproval concret) ; à reconsidérer si un module
/// dédié émerge. Aucune sémantique fiscale n'est portée ici (décision de placement structurel, jamais une règle
/// inventée — CLAUDE.md n°2).
/// </para>
/// Toujours scopé par <paramref name="companyId"/> (CLAUDE.md n°9). Fail-closed (CLAUDE.md n°3).
/// </summary>
public interface IMultiPartySignatureGate
{
    /// <summary>
    /// Statue sur l'ouverture du gate de co-signature N parties du document <paramref name="documentId"/>
    /// (complétude des slots × niveau de preuve requis du tenant PAR slot), sur sa tentative la plus récente.
    /// Fail-closed si aucune validation n'existe.
    /// </summary>
    Task<DocumentGateDecision> EvaluateAsync(Guid companyId, Guid documentId, CancellationToken ct = default);
}
