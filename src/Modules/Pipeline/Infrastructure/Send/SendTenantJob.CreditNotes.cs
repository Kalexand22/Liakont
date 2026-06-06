namespace Liakont.Modules.Pipeline.Infrastructure.Send;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Documents.Contracts.Lifecycle;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Pipeline.Infrastructure.Check;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// PIP02 — RÉORDONNANCEMENT DES AVOIRS (F07 §B.5). Au CHECK, un avoir (référence d'origine EN 16931 BG-3 /
/// BT-25) dont la facture d'origine n'est pas (encore) émise est bloqué (<c>CREDIT_NOTE_ORPHAN</c> /
/// <c>CREDIT_NOTE_ORIGINAL_NOT_ISSUED</c>, VAL04). Comme l'événement d'ingestion ne se produit qu'UNE fois,
/// rien ne ré-évaluerait cet avoir une fois l'origine émise : il resterait bloqué pour toujours. Cette passe,
/// exécutée par le SEND APRÈS l'émission des factures d'origine, ré-évalue les avoirs bloqués et débloque
/// (<c>Blocked → ReadyToSend</c>) ceux dont l'origine est désormais émise — l'avoir part alors à la seconde
/// passe d'envoi, TOUJOURS après sa facture d'origine.
/// </summary>
public sealed partial class SendTenantJob
{
    /// <summary>
    /// Ré-évalue les AVOIRS restés <c>Blocked</c> et débloque ceux qui passent désormais (facture d'origine
    /// émise). Tenant-scopé (services du scope courant). Retourne les identifiants des avoirs débloqués.
    /// </summary>
    /// <remarks>
    /// <para>SCOPE STRICT aux avoirs : un document est ré-évalué seulement si son pivot stagé porte au moins une
    /// référence d'origine (<see cref="Liakont.Agent.Contracts.Pivot.PivotDocumentDto.CreditNoteRefs"/>, le
    /// signal structurel EN 16931 d'un avoir). Un document bloqué pour une AUTRE raison (mapping, table absente,
    /// garde-fou production…) n'est PAS touché ici — son déblocage relève d'un geste opérateur ou d'un autre item.</para>
    /// <para>RE-ÉVALUATION COMPLÈTE (mapping + garde-fou + validation, via <see cref="DocumentCheckEvaluator"/>),
    /// jamais un simple test de l'origine : un avoir bloqué pour un AUTRE motif (montant négatif, mapping…) DOIT
    /// rester bloqué (« bloquer plutôt qu'envoyer faux », CLAUDE.md n°3). La seule transition possible est
    /// <c>Blocked → ReadyToSend</c> (la machine à états interdit <c>Blocked → Blocked</c>) : un avoir toujours
    /// bloqué ne subit AUCUNE transition (pas de re-blocage, pas de churn de la piste d'audit append-only).</para>
    /// <para>COÛT V1 (tracé, dette assumée) : le balayage relit le staging de CHAQUE document bloqué pour écarter
    /// les non-avoirs (la discrimination « avoir » n'est portée que par le pivot stagé, non requêtable en SQL).
    /// Borné en pratique par le backlog de documents bloqués (supervisé par l'opérateur). Optimisation future :
    /// persister au CHECK un marqueur léger « porte des références d'origine » (Documents.Contracts) pour filtrer
    /// en requête tenant-scopée et éviter la relecture du staging des non-avoirs.</para>
    /// </remarks>
    private static async Task<List<Guid>> ReconcileCreditNotesAsync(
        IServiceProvider services,
        Guid companyId,
        string tenantId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var blockedIds = await SnapshotIdsByStateAsync(services, BlockedStateName, cancellationToken);
        if (blockedIds.Count == 0)
        {
            return new List<Guid>();
        }

        var queries = services.GetRequiredService<IDocumentQueries>();
        var lifecycle = services.GetRequiredService<IDocumentLifecycle>();
        var unblocked = new List<Guid>();

        foreach (var documentId in blockedIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var document = await queries.GetByIdAsync(documentId, cancellationToken);
                if (document is null || !string.Equals(document.State, BlockedStateName, StringComparison.Ordinal))
                {
                    // État changé entre le snapshot et le traitement (rejeu / geste opérateur) : on ne touche pas.
                    continue;
                }

                var staged = await ReadStagedPivotAsync(services, tenantId, document, logger, cancellationToken);
                if (staged.Status != StagedReadStatus.Ok)
                {
                    // Absent = transitoire (repris au prochain cycle) ; intégrité = laissé Blocked (déjà signalé au CHECK).
                    continue;
                }

                // Détection d'un avoir par le signal structurel EN 16931 (CreditNoteRefs) — pas par le type source brut
                // (correspondance type→avoir non spécifiée, varie par logiciel — VAL04 / F07-F08 §B.0, CLAUDE.md n°2).
                if (staged.Pivot!.CreditNoteRefs.Count == 0)
                {
                    continue;
                }

                var decision = await DocumentCheckEvaluator.EvaluateAsync(
                    services, companyId, document.DocumentNumber, staged.Pivot!, cancellationToken);

                if (decision.IsReady)
                {
                    await lifecycle.MarkReadyToSendAsync(document.Id, decision.MappingVersion!, cancellationToken);
                    unblocked.Add(document.Id);
                    LogCreditNoteUnblocked(logger, document.Id);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Isolation PAR DOCUMENT (même convention que SafeProcessAsync) : un avoir qui lève (course TOCTOU
                // Blocked→non-Blocked entre la relecture d'état et la transition, lecture/validation en échec) n'avorte
                // NI la réconciliation des autres avoirs NI l'écriture de la trace SEND — il est repris au cycle suivant.
                LogCreditNoteReconcileFailed(logger, documentId, ex);
            }
        }

        return unblocked;
    }

    [LoggerMessage(EventId = 7213, Level = LogLevel.Information,
        Message = "SEND : avoir {DocumentId} débloqué — sa facture d'origine est désormais émise (Blocked → ReadyToSend, réordonnancement F07).")]
    private static partial void LogCreditNoteUnblocked(ILogger logger, Guid documentId);

    [LoggerMessage(EventId = 7214, Level = LogLevel.Warning,
        Message = "SEND : échec de réconciliation de l'avoir {DocumentId} — ignoré ce cycle, réordonnancement des autres avoirs poursuivi.")]
    private static partial void LogCreditNoteReconcileFailed(ILogger logger, Guid documentId, Exception exception);
}
