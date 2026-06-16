namespace Liakont.Modules.Mandats.Infrastructure;

using Liakont.Modules.Mandats.Contracts;
using Liakont.Modules.Mandats.Contracts.Queries;

/// <summary>
/// Implémentation de la garde d'émission (ADR-0024 §3, MND03) : lit l'acceptation du document via
/// <see cref="ISelfBilledAcceptanceQueries"/> (déjà tenant-scopée, INV-MANDATS-1) et autorise l'émission
/// UNIQUEMENT si le gate est ouvert (<see cref="SelfBilledAcceptanceDto.IsAccepted"/> = Accepted/TacitlyAccepted).
/// Aucune acceptation enregistrée ⇒ non autorisé (fail-closed, « bloquer plutôt qu'émettre faux » — CLAUDE.md n°3).
/// La règle « gate ouvert » n'est PAS dupliquée ici : elle est portée par <c>IsAccepted</c> (MND02).
/// </summary>
internal sealed class SelfBilledGate : ISelfBilledGate
{
    private readonly ISelfBilledAcceptanceQueries _acceptances;

    public SelfBilledGate(ISelfBilledAcceptanceQueries acceptances) => _acceptances = acceptances;

    /// <inheritdoc />
    public async Task<SelfBilledGateDecision> EvaluateEmissionAsync(Guid companyId, Guid documentId, CancellationToken ct = default)
    {
        var acceptance = await _acceptances.GetAcceptance(companyId, documentId, ct);
        return new SelfBilledGateDecision
        {
            IsEmissionAllowed = acceptance?.IsAccepted ?? false,
            AcceptanceState = acceptance?.State,
        };
    }
}
