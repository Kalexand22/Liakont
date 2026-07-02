namespace Liakont.Modules.Pipeline.Infrastructure.B2cReporting;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Pipeline.Application;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Domain;
using Liakont.Modules.Pipeline.Domain.B2cReporting;
using Liakont.Modules.Staging.Contracts;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.Transmission.Contracts;
using Liakont.Modules.TvaMapping.Contracts.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Jobs;

/// <summary>
/// E-reporting B2C de la MARGE (flux 10.3, enchères — B4) : job planifié PAR TENANT (mécanique
/// <see cref="ITenantJob"/>/<c>TenantJobRunner</c>, SOL06 ; JAMAIS une boucle multi-tenant maison). Pour le
/// tenant courant : découvre les documents B2C-marge prêts (marqueur <c>IsB2cReportingDeclaration</c> + frais
/// acheteur/vendeur), lit leur pivot stagé, résout le taux des honoraires (mapping F03, <c>Part.Frais</c>),
/// résout la marge (cœur PUR <see cref="B2cMarginResolver"/>, fail-closed), agrège jour×devise×taux
/// (<see cref="B2cTransactionAggregationCalculator"/>), puis TRANSMET chaque agrégat à la PA active
/// (<see cref="IPaClient.SendB2cTransactionAsync"/>).
/// <para>
/// <b>Anti-doublon (décision D3, attempt-once par document)</b> : l'API SuperPDP n'a aucune clé d'idempotence
/// (2 POST = 2 lignes). Avant chaque POST, une entrée <see cref="B2cMarginEmissionStatus.Pending"/> est écrite
/// pour chaque document de l'agrégat (<see cref="IB2cMarginEmissionStore"/>, append-only) : un document déjà
/// tenté est EXCLU des runs suivants — même en cas de crash après le POST (jamais 2 POST). Un document tardif
/// sur un jour déjà émis part dans un NOUVEL agrégat (SuperPDP ré-agrège côté serveur) : pas de sous-déclaration.
/// </para>
/// <para>
/// <b>Frontières</b> : tenant-scopé (companyId du profil tenant, connexion routée — CLAUDE.md n°9) ; aucune
/// référence à un plug-in PA concret (capacité résolue via <see cref="IPaClientRegistry"/>, gate de capacité
/// dédiée <c>SupportsMarginAmountReporting</c>) ; aucune règle fiscale inventée (taux du mapping F03, forme
/// ancrée F03 §2.5/§2.6) ; la traçabilité (D2) gèle le lien reporting↔pièce APRÈS confirmation d'envoi, au grain
/// document (export <c>GetByDocumentAsync</c> préservé). Une trace d'exécution est écrite (<c>pipeline.run_logs</c>).
/// </para>
/// </summary>
public sealed partial class B2cMarginAggregatorTenantJob : ITenantJob
{
    private const string ReadyToSendStateName = "ReadyToSend";
    private const int PageSize = 200;

    private readonly PipelineRunTrigger _trigger;

    /// <summary>Construit le job d'e-reporting B2C de la marge d'un tenant.</summary>
    /// <param name="trigger">Origine du déclenchement (planifié / manuel) — tracée dans le journal d'exécutions.</param>
    public B2cMarginAggregatorTenantJob(PipelineRunTrigger trigger = PipelineRunTrigger.Scheduled)
    {
        _trigger = trigger;
    }

    /// <inheritdoc />
    public string Name => "pipeline.aggregate-b2c-margin";

    /// <inheritdoc />
    public async Task ExecuteAsync(TenantJobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var services = context.Services;
        var tenantId = context.TenantId;
        var timeProvider = services.GetRequiredService<TimeProvider>();
        var logger = services.GetRequiredService<ILogger<B2cMarginAggregatorTenantJob>>();
        var startedAt = timeProvider.GetUtcNow();

        var tenantSettings = services.GetRequiredService<ITenantSettingsQueries>();
        var companyId = await tenantSettings.GetCurrentCompanyId(cancellationToken);
        if (companyId is null)
        {
            // Profil tenant pas encore créé (CFG02) : rien à déclarer (transitoire).
            await WriteRunLogAsync(services, timeProvider, _trigger, startedAt, 0, 0, 0, "E-reporting B2C marge : aucun profil tenant (companyId) — rien à déclarer.", cancellationToken);
            return;
        }

        // 0) Rattrapage AVANT toute nouvelle émission (ADR-0037 D3) : réconcilie l'état résiduel « émission
        //    ACCEPTÉE (journal Issued) mais document resté ReadyToSend » d'un run précédent interrompu (fenêtre de
        //    crash/annulation) — rejoue la SEULE transition d'état (idempotente, non-throwante), JAMAIS un re-POST
        //    (ces documents sont dans `handled` → exclus de la découverte). Agnostique au canal : ce job tourne pour
        //    CHAQUE tenant à chaque cadence (fan-out), il porte donc le filet permanent des 4 voies e-reporting B2C.
        var handled = await services.GetRequiredService<IB2cMarginEmissionStore>().GetHandledDocumentIdsAsync(cancellationToken);
        await B2cReportingEmitter.ReconcileResidualEReportsAsync(services, handled, logger, cancellationToken);

        // 1) Découverte + résolution + agrégation (cœur PUR fail-closed). Aucun envoi tant que la marge n'est pas résolue.
        var discovery = await DiscoverContributionsAsync(services, tenantId, companyId.Value, handled, logger, cancellationToken);
        var transactions = B2cTransactionAggregationCalculator.Aggregate(discovery.Contributions);

        // 2) Gate de capacité AVANT toute écriture d'émission : une PA sans capacité marge ne reçoit RIEN et
        //    AUCUN document n'est marqué tenté (il reste repris au prochain run quand la capacité sera là).
        var paClient = await B2cReportingEmitter.ResolveActivePaClientAsync(services, tenantSettings, companyId.Value, tenantId, cancellationToken);
        if (paClient is null || !paClient.Capabilities.SupportsB2cReporting || !paClient.Capabilities.SupportsMarginAmountReporting)
        {
            var pendingDetail = Describe(discovery, transactions.Count, issued: 0, rejected: 0, technical: 0, capabilityPending: true);
            await WriteRunLogAsync(services, timeProvider, _trigger, startedAt, discovery.Examined, 0, discovery.Blocked.Count, pendingDetail, cancellationToken);
            LogCapabilityPending(logger, tenantId, transactions.Count);
            return;
        }

        // 3) Émission (TMA1 / SE, enchères F03 §2.5) : par agrégat, Pending (crash-safe) → POST → issue → gel des
        //    liens (D2) si Issued — orchestration PARTAGÉE (B2cReportingEmitter) ; seuls catégorie/rôle distinguent la marge.
        var emissionStore = services.GetRequiredService<IB2cMarginEmissionStore>();
        var (issued, rejected, technical) = await B2cReportingEmitter.EmitAllAsync(
            services,
            emissionStore,
            paClient,
            companyId.Value,
            transactions,
            EReportingTransactionCategory.Tma1,
            EReportingDeclarantRole.Seller,
            logger,
            cancellationToken);

        var detail = Describe(discovery, transactions.Count, issued, rejected, technical, capabilityPending: false);
        await WriteRunLogAsync(services, timeProvider, _trigger, startedAt, discovery.Examined, issued, discovery.Blocked.Count + rejected + technical, detail, cancellationToken);
        LogCompleted(logger, tenantId, transactions.Count, issued, rejected, technical);
    }

    // Découvre les documents B2C-marge prêts (non encore tentés), lit leur pivot stagé, résout taux + marge, et
    // produit les contributions. Isolation par document (un document en erreur n'arrête pas le run). N+1 assumé
    // (dette PIP03a-like) : la lecture est bornée par l'état ReadyToSend, pré-fenêtrage du job.
    private static async Task<DiscoveryResult> DiscoverContributionsAsync(
        IServiceProvider services,
        string tenantId,
        Guid companyId,
        IReadOnlySet<Guid> handled,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var documents = services.GetRequiredService<IDocumentQueries>();
        var staging = services.GetRequiredService<IPayloadStagingStore>();
        var tvaMapping = services.GetRequiredService<ITvaMappingService>();

        var contributions = new List<B2cMarginContribution>();
        var blocked = new List<B2cMarginBlockReason>();
        var examined = 0;

        for (var page = 1; ; page++)
        {
            var summaries = await documents.GetByStateAsync(ReadyToSendStateName, page, PageSize, cancellationToken);
            if (summaries.Count == 0)
            {
                break;
            }

            foreach (var summary in summaries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (handled.Contains(summary.Id))
                {
                    continue; // attempt-once (D3) : déjà tenté → jamais re-POSTé.
                }

                try
                {
                    var document = await documents.GetByIdAsync(summary.Id, cancellationToken);
                    if (document is null)
                    {
                        continue;
                    }

                    var pivot = await B2cReportingDiscovery.TryReadStagedPivotAsync(staging, tenantId, document, logger, cancellationToken);
                    if (pivot is null || !B2cReportingDiscovery.HasFees(pivot))
                    {
                        continue; // non stagé/altéré, ou sans frais (jamais une marge) — pré-filtre cheap avant tout mapping.
                    }

                    // Marqueur 10.3-marge DÉRIVÉ read-time via le mapping VALIDÉ (catégorie + VATEX), comme au
                    // CHECK/SEND : le pivot stagé est le pivot SOURCE (régimes bruts, jamais marqué par l'agent).
                    // On l'enrichit ICI par le MÊME moteur (CheckTvaMapping), qui pose le marqueur — une seule
                    // source de la classification, jamais inventée (F03).
                    var marked = await B2cReportingDiscovery.EnrichForB2cMarkingAsync(tvaMapping, companyId, pivot, cancellationToken);
                    if (marked is null)
                    {
                        // Doc PORTEUR DE FRAIS dont l'adjudication n'est plus mappable depuis le CHECK (table
                        // absente / régime décroché / ligne hors forme) : TRACÉ (jamais un skip muet), miroir du
                        // HOLD TvaUnresolved de SendTenantJob. Le doc reste ReadyToSend, repris quand la table est rétablie.
                        blocked.Add(B2cMarginBlockReason.AdjudicationNotMapped);
                        LogMarginAdjudicationNotMapped(logger, summary.Id);
                        continue;
                    }

                    if (!B2cMarginDeclaration.Matches(marked))
                    {
                        continue; // classé NON-marge (taxable / acheteur pro) → voie document (D1) : pas une anomalie, pas tracé.
                    }

                    examined++;
                    var resolution = await B2cMarginDocumentResolver.ResolveAsync(tvaMapping, companyId, marked, cancellationToken);
                    if (resolution.IsResolved)
                    {
                        contributions.Add(new B2cMarginContribution
                        {
                            DocumentId = document.Id,
                            SourceReference = pivot.SourceReference,
                            Date = document.IssueDate,
                            CurrencyCode = pivot.CurrencyCode,
                            MarginTtc = resolution.MarginTtc,
                            RatePercent = resolution.RatePercent,
                        });
                    }
                    else if (resolution.BlockReason is { } reason)
                    {
                        blocked.Add(reason);
                        LogMarginBlocked(logger, summary.Id, reason.ToString());
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    LogDocumentFailed(logger, summary.Id, ex);
                }
            }

            if (summaries.Count < PageSize)
            {
                break;
            }
        }

        return new DiscoveryResult(examined, contributions, blocked);
    }

    private static string Describe(
        DiscoveryResult discovery,
        int aggregates,
        int issued,
        int rejected,
        int technical,
        bool capabilityPending)
    {
        var blockedBreakdown = discovery.Blocked
            .GroupBy(reason => reason)
            .Select(g => $"{g.Key}={g.Count()}")
            .ToList();
        var blocked = blockedBreakdown.Count > 0 ? string.Join(", ", blockedBreakdown) : "aucun";

        if (capabilityPending)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"E-reporting B2C marge (flux 10.3) : {aggregates} agrégat(s) jour×devise EN ATTENTE de capacité PA (SupportsMarginAmountReporting) — aucun envoi. {discovery.Examined} document(s) marge examiné(s), {discovery.Blocked.Count} bloqué(s) ({blocked}).");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"E-reporting B2C marge (flux 10.3) : {aggregates} agrégat(s) jour×devise — {issued} transmis, {rejected} rejeté(s), {technical} erreur(s). {discovery.Examined} document(s) marge examiné(s), {discovery.Blocked.Count} bloqué(s) ({blocked}).");
    }

    private static async Task WriteRunLogAsync(
        IServiceProvider services,
        TimeProvider timeProvider,
        PipelineRunTrigger trigger,
        DateTimeOffset startedAt,
        int processed,
        int succeeded,
        int failed,
        string detail,
        CancellationToken cancellationToken)
    {
        var runLog = RunLog.Start(PipelineRunType.B2cMarginAggregate, trigger, startedAt);
        runLog.Complete(
            completedAt: timeProvider.GetUtcNow(),
            documentsProcessed: processed,
            documentsSucceeded: succeeded,
            documentsFailed: failed,
            detail: detail);
        await services.GetRequiredService<IPipelineRunLogStore>().SaveAsync(runLog, cancellationToken);
    }

    [LoggerMessage(EventId = 7440, Level = LogLevel.Information,
        Message = "E-reporting B2C marge terminé pour le tenant « {TenantId} » : {Aggregates} agrégat(s) — {Issued} transmis, {Rejected} rejeté(s), {Technical} erreur(s).")]
    private static partial void LogCompleted(ILogger logger, string tenantId, int aggregates, int issued, int rejected, int technical);

    [LoggerMessage(EventId = 7441, Level = LogLevel.Information,
        Message = "E-reporting B2C marge pour le tenant « {TenantId} » : {Aggregates} agrégat(s) en attente — la PA active ne déclare pas la capacité de report du montant de marge (aucun envoi).")]
    private static partial void LogCapabilityPending(ILogger logger, string tenantId, int aggregates);

    [LoggerMessage(EventId = 7442, Level = LogLevel.Warning,
        Message = "E-reporting B2C marge : document « {DocumentId} » bloqué (fail-closed) — motif {Reason}. Aucune transmission ; action opérateur requise.")]
    private static partial void LogMarginBlocked(ILogger logger, Guid documentId, string reason);

    [LoggerMessage(EventId = 7443, Level = LogLevel.Error,
        Message = "E-reporting B2C marge : échec du traitement du document « {DocumentId} » — isolé, le run continue.")]
    private static partial void LogDocumentFailed(ILogger logger, Guid documentId, Exception exception);

    [LoggerMessage(EventId = 7446, Level = LogLevel.Warning,
        Message = "E-reporting B2C marge : document « {DocumentId} » porteur de frais dont l'adjudication n'est plus mappable (table de mapping TVA modifiée depuis le contrôle) — non agrégé, tracé. Le document reste prêt à l'envoi ; action opérateur : faites valider/rétablir le mapping du régime de cette adjudication.")]
    private static partial void LogMarginAdjudicationNotMapped(ILogger logger, Guid documentId);

    private sealed record DiscoveryResult(int Examined, IReadOnlyList<B2cMarginContribution> Contributions, IReadOnlyList<B2cMarginBlockReason> Blocked);
}
