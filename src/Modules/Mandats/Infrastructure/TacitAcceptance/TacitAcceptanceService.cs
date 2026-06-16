namespace Liakont.Modules.Mandats.Infrastructure.TacitAcceptance;

using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.DocumentApproval.Contracts.DTOs;
using Liakont.Modules.DocumentApproval.Contracts.Queries;

/// <summary>
/// Implémente la bascule tacite <c>PendingAcceptance → TacitlyAccepted</c> (MND04, ADR-0024 §4 / F15 §2.3) pour
/// le tenant courant. Depuis SIG05, l'acceptation est PROJETÉE via DocumentApproval (purpose
/// <see cref="ValidationPurpose.SelfBilledAcceptance"/>) : le service énumère les candidats dus via
/// <see cref="IDocumentApprovalQueries.ListTacitDueDocumentsAsync"/> puis bascule chacun via
/// <see cref="IDocumentApprovalWorkflow.RecordTacitValidationIfDueAsync"/> (verrou + re-vérification d'éligibilité
/// SOUS VERROU, anti-TOCTOU). La condition fiscale « mandat écrit ET délai non null » est ENCODÉE dans
/// <c>DeadlineUtc</c> (calculée à la création, F15 §2.3 / INV-ACCEPT-3) : <c>DeadlineUtc != null</c> ⟺ bascule
/// tacite possible. Sous mandat tacite ou délai null, aucune échéance, donc jamais candidat — seule l'acceptation
/// EXPRESSE débloque (BOFiP §290, CLAUDE.md n°2/3, jamais affaibli). La machine + le journal append-only
/// (<c>document_approval_log</c>) sont ceux de DocumentApproval (INV-ACCEPT-5 amendé).
/// </summary>
internal sealed class TacitAcceptanceService : ITacitAcceptanceService
{
    /// <summary>Origine système tracée au journal (operator_id null = bascule par job, pas un opérateur humain).</summary>
    internal const string TacitOperatorName = "Bascule tacite (job)";

    private readonly IDocumentApprovalQueries _approvalQueries;
    private readonly IDocumentApprovalWorkflow _workflow;
    private readonly TimeProvider _timeProvider;

    public TacitAcceptanceService(
        IDocumentApprovalQueries approvalQueries,
        IDocumentApprovalWorkflow workflow,
        TimeProvider timeProvider)
    {
        _approvalQueries = approvalQueries;
        _workflow = workflow;
        _timeProvider = timeProvider;
    }

    public async Task<TacitAcceptanceRunResult> ProcessDueAsync(CancellationToken ct = default)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        // Snapshot des clés dues AVANT traitement (l'état traité se vide au fil des bascules : énumérer puis
        // re-paginer un état qui change est un faux-vert connu — voir lessons pipeline tenant-job).
        IReadOnlyList<TacitDueDocumentDto> candidates =
            await _approvalQueries.ListTacitDueDocumentsAsync(ValidationPurpose.SelfBilledAcceptance, now, ct);

        var tacitlyAccepted = 0;
        foreach (TacitDueDocumentDto candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();

            // Bascule SI ÉLIGIBLE : verrou + re-vérification SOUS VERROU (Pending & échéance échue), transition +
            // journal système dans la MÊME transaction (DocumentApproval). Plus éligible (acceptation expresse /
            // contestation concurrente entre l'énumération et le verrou) ⇒ no-op sans effet.
            var transitioned = await _workflow.RecordTacitValidationIfDueAsync(
                candidate.CompanyId,
                candidate.DocumentId,
                ValidationPurpose.SelfBilledAcceptance,
                now,
                TacitOperatorName,
                ct);

            if (transitioned)
            {
                tacitlyAccepted++;
            }
        }

        return new TacitAcceptanceRunResult(candidates.Count, tacitlyAccepted);
    }
}
