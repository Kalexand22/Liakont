namespace Liakont.Modules.Mandats.Infrastructure;

using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.Mandats.Contracts;

/// <summary>
/// Port de gate de co-signature N parties (SIG06) : délègue la Règle de gate générique (ADR-0028 §5/§8 ;
/// complétude + niveau de preuve PAR slot) au module DocumentApproval PAR SES CONTRACTS
/// (<see cref="IDocumentApprovalGate"/>) pour le purpose <see cref="ValidationPurpose.MultiPartySignature"/>.
/// </summary>
internal sealed class MultiPartySignatureGate : IMultiPartySignatureGate
{
    private readonly IDocumentApprovalGate _gate;

    public MultiPartySignatureGate(IDocumentApprovalGate gate) => _gate = gate;

    public async Task<DocumentGateDecision> EvaluateAsync(Guid companyId, Guid documentId, CancellationToken ct = default)
    {
        var result = await _gate.EvaluateAsync(companyId, documentId, ValidationPurpose.MultiPartySignature, ct);
        return new DocumentGateDecision { IsOpen = result.IsOpen, Reason = result.Reason };
    }
}
