namespace Liakont.Modules.Mandats.Infrastructure.Queries;

using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.DocumentApproval.Contracts.DTOs;
using Liakont.Modules.DocumentApproval.Contracts.Queries;
using Liakont.Modules.Mandats.Contracts.DTOs;
using Liakont.Modules.Mandats.Contracts.Queries;
using Liakont.Modules.Mandats.Infrastructure;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Lectures (seules) de l'acceptation des auto-factures sous mandat (SIG05). PROJECTION restreinte du module
/// générique DocumentApproval : l'ÉTAT et le JOURNAL sont lus via <see cref="IDocumentApprovalQueries"/>
/// (purpose <see cref="ValidationPurpose.SelfBilledAcceptance"/>) et projetés dans le vocabulaire fiscal
/// (<see cref="SelfBilledAcceptanceStateMap"/>) ; la companion fiscale du module Mandats (BT-1 alloué /
/// pending_since) est lue séparément. Toujours scopé par <c>company_id</c> (CLAUDE.md n°9/17, INV-MANDATS-1) —
/// aucune lecture cross-tenant. La règle « gate ouvert » (<c>IsAccepted</c>) n'est pas dupliquée : elle dérive
/// de l'état projeté (Accepted/TacitlyAccepted), seule source restant la machine DocumentApproval.
/// </summary>
internal sealed class PostgresSelfBilledAcceptanceQueries : ISelfBilledAcceptanceQueries
{
    private readonly IDocumentApprovalQueries _approvalQueries;
    private readonly IConnectionFactory _connectionFactory;

    public PostgresSelfBilledAcceptanceQueries(
        IDocumentApprovalQueries approvalQueries, IConnectionFactory connectionFactory)
    {
        _approvalQueries = approvalQueries;
        _connectionFactory = connectionFactory;
    }

    public async Task<SelfBilledAcceptanceDto?> GetAcceptance(
        Guid companyId, Guid documentId, CancellationToken ct = default)
    {
        var validation = await _approvalQueries.GetLatestAttempt(
            companyId, documentId, ValidationPurpose.SelfBilledAcceptance, ct);
        if (validation is null)
        {
            return null;
        }

        var state = SelfBilledAcceptanceStateMap.FromValidationStateName(validation.State);

        using var connection = await _connectionFactory.OpenAsync(ct);
        var companion = await SelfBilledAcceptanceCompanionReader.LoadAsync(
            connection, companyId, documentId, transaction: null, ct);

        if (companion is null)
        {
            // Incohérence INATTEIGNABLE par construction (OpenPendingAsync écrit la companion AVANT la validation,
            // et V010 la conserve). Si elle survenait, on DÉGRADE FAIL-CLOSED : retourner null = « aucune
            // acceptation » → le gate (ISelfBilledGate) bloque l'émission (CLAUDE.md n°3), au lieu de lever une
            // exception bruyante sur le chemin CHECK/SEND du pipeline. On NE fabrique JAMAIS pending_since/BT-1
            // à partir de l'échéance (correction review round 1 : ne pas inventer de donnée fiscale).
            return null;
        }

        return new SelfBilledAcceptanceDto
        {
            DocumentId = documentId,
            State = state.ToString(),
            AllocatedNumber = companion.Value.AllocatedNumber,
            PendingSince = companion.Value.PendingSince,
            DeadlineUtc = validation.DeadlineUtc,
            IsAccepted = SelfBilledAcceptanceStateMap.IsAccepted(state),
        };
    }

    public async Task<IReadOnlyList<SelfBilledAcceptanceLogEntryDto>> GetAcceptanceLog(
        Guid companyId, Guid documentId, CancellationToken ct = default)
    {
        var log = await _approvalQueries.GetApprovalLog(
            companyId, documentId, ValidationPurpose.SelfBilledAcceptance, ct);

        var result = new List<SelfBilledAcceptanceLogEntryDto>(log.Count);
        foreach (DocumentApprovalLogEntryDto entry in log)
        {
            result.Add(new SelfBilledAcceptanceLogEntryDto
            {
                DocumentId = entry.DocumentId,
                FromState = SelfBilledAcceptanceStateMap.NameOrNull(entry.FromState),
                ToState = SelfBilledAcceptanceStateMap.FromValidationStateName(entry.ToState).ToString(),
                OperatorId = entry.OperatorId,
                OperatorName = entry.OperatorName,
                OccurredAt = entry.OccurredAt,
            });
        }

        return result;
    }
}
