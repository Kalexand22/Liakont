namespace Liakont.Modules.Mandats.Infrastructure;

using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.Mandats.Contracts;

/// <summary>
/// Port de gate d'acceptation de l'avoir auto-facturé 261 (SIG06, ❓ #9 F15 §6.5 — défaut défendable « oui » :
/// même discipline d'acceptation que le 389, conservateur, aucune valeur fiscale inventée — CLAUDE.md n°2).
/// Délègue la Règle de gate générique (ADR-0028 §5) au module DocumentApproval PAR SES CONTRACTS
/// (<see cref="IDocumentApprovalGate"/>) pour le purpose <see cref="ValidationPurpose.CreditNoteAcceptance"/>.
/// </summary>
internal sealed class CreditNoteAcceptanceGate : ICreditNoteAcceptanceGate
{
    private readonly IDocumentApprovalGate _gate;

    public CreditNoteAcceptanceGate(IDocumentApprovalGate gate) => _gate = gate;

    public async Task<DocumentGateDecision> EvaluateAsync(Guid companyId, Guid documentId, CancellationToken ct = default)
    {
        var result = await _gate.EvaluateAsync(companyId, documentId, ValidationPurpose.CreditNoteAcceptance, ct);
        return new DocumentGateDecision { IsOpen = result.IsOpen, Reason = result.Reason };
    }
}
