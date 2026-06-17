namespace Liakont.Modules.Mandats.Infrastructure;

using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.Mandats.Contracts;
using Liakont.Modules.Mandats.Contracts.Queries;

/// <summary>
/// Garde d'émission self-billed (ADR-0024 §3, MND03, INV-ACCEPT-2). Depuis SIG06, l'émissibilité est tranchée par
/// la Règle de gate GÉNÉRIQUE câblée de bout en bout (<see cref="IDocumentApprovalGate"/>, ADR-0028 §5) pour le
/// purpose <see cref="ValidationPurpose.SelfBilledAcceptance"/> : état ∈ {Validated, TacitlyValidated} ET niveau
/// de preuve ≥ exigence TENANT (défaut Recorded) ET forme expresse (sur transition Validated). Le niveau requis
/// est un PARAMÉTRAGE TENANT (jamais un défaut produit — CLAUDE.md n°2/3) : un tenant en Recorded n'est jamais
/// bloqué du seul fait de l'absence de fournisseur de signature, et un tenant qui durcit (AES) bloque un Recorded
/// nu. Aucune acceptation enregistrée ⇒ non autorisé (fail-closed, CLAUDE.md n°3).
/// <para>
/// L'état fiscal d'acceptation (<see cref="SelfBilledGateDecision.AcceptanceState"/>) est lu en plus via la
/// projection (<see cref="ISelfBilledAcceptanceQueries"/>) pour que le pipeline compose le message opérateur exact
/// (en attente / contestée / aucune) — il dérive de la MÊME donnée DocumentApproval que le gate (cohérence).
/// </para>
/// </summary>
internal sealed class SelfBilledGate : ISelfBilledGate
{
    private readonly IDocumentApprovalGate _gate;
    private readonly ISelfBilledAcceptanceQueries _acceptances;

    public SelfBilledGate(IDocumentApprovalGate gate, ISelfBilledAcceptanceQueries acceptances)
    {
        _gate = gate;
        _acceptances = acceptances;
    }

    /// <inheritdoc />
    public async Task<SelfBilledGateDecision> EvaluateEmissionAsync(Guid companyId, Guid documentId, CancellationToken ct = default)
    {
        var decision = await _gate.EvaluateAsync(companyId, documentId, ValidationPurpose.SelfBilledAcceptance, ct);
        var acceptance = await _acceptances.GetAcceptance(companyId, documentId, ct);
        return new SelfBilledGateDecision
        {
            IsEmissionAllowed = decision.IsOpen,
            AcceptanceState = acceptance?.State,
        };
    }
}
