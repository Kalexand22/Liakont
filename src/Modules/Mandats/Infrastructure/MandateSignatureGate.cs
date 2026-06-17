namespace Liakont.Modules.Mandats.Infrastructure;

using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.Mandats.Contracts;

/// <summary>
/// Port de gate de signature du mandat (SIG06) : délègue la Règle de gate générique (ADR-0028 §5) au module
/// DocumentApproval PAR SES CONTRACTS (<see cref="IDocumentApprovalGate"/>, frontière module-rules §3) pour le
/// purpose <see cref="ValidationPurpose.MandateSignature"/>. Aucune règle dupliquée ; le niveau requis est résolu
/// côté DocumentApproval (paramétrage tenant, défaut Recorded).
/// </summary>
internal sealed class MandateSignatureGate : IMandateSignatureGate
{
    private readonly IDocumentApprovalGate _gate;

    public MandateSignatureGate(IDocumentApprovalGate gate) => _gate = gate;

    public async Task<DocumentGateDecision> EvaluateAsync(Guid companyId, Guid documentId, CancellationToken ct = default)
    {
        var result = await _gate.EvaluateAsync(companyId, documentId, ValidationPurpose.MandateSignature, ct);
        return new DocumentGateDecision { IsOpen = result.IsOpen, Reason = result.Reason };
    }
}
