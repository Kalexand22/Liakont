namespace Liakont.Modules.Pipeline.Infrastructure.B2cReporting;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Documents.Contracts.DTOs;
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
/// E-reporting B2C d'un document ORDINAIRE taxable (flux 10.3, F03 §2.9) : job planifié PAR TENANT (mécanique
/// <see cref="ITenantJob"/>/<c>TenantJobRunner</c>, SOL06 ; JAMAIS une boucle multi-tenant maison). Couvre les
/// documents que l'OVV émet DIRECTEMENT, HORS mécanisme d'enchères opaque (SANS frais) : <b>factures clients</b>
/// (livraison de biens → <c>TLB1</c>) et <b>notes d'honoraires d'inventaire</b> (prestation de services →
/// <c>TPS1</c>). Apparenté à <see cref="B2cTaxableAggregatorTenantJob"/> (même flux, même agrégation
/// <see cref="B2cTaxableAggregationCalculator"/>, même orchestration d'émission PARTAGÉE
/// <see cref="B2cReportingEmitter"/>) avec DEUX différences (F03 §2.9) :
/// <list type="bullet">
///   <item><b>Base = prix total des LIGNES</b> (pas de spine enchères) : adjudication HT/TVA SOURCÉE par ligne,
///   AUCUNE commission acheteur (honoraire TTC = 0). La TVA est distincte, reprise telle quelle (ADR-0015).</item>
///   <item><b>TT-81 par NATURE de l'opération</b> (<see cref="PivotDocumentDto.OperationCategory"/>, posée par
///   l'agent) : <c>LivraisonBiens → TLB1</c>, <c>PrestationServices → TPS1</c> (G1.68). <c>Mixte</c>/inconnu →
///   FAIL-CLOSED tracé (le pivot ne porte pas le bien/service PAR LIGNE → ventilation TT-81 non déterminable,
///   jamais devinée — n°2). Un run mêlant biens/services émet un POST par TT-81 (groupé).</item>
/// </list>
/// <para>
/// <b>Anti-doublon</b> : MÊME journal d'émission append-only que les autres flux B2C (attempt-once par document,
/// décision D3) — un document n'emprunte qu'UNE voie (marge/prix total/export/ordinaire), et un document déjà
/// tenté est EXCLU des runs suivants (jamais 2 POST).
/// </para>
/// <para>
/// <b>Frontières</b> : tenant-scopé (companyId du profil tenant — CLAUDE.md n°9) ; aucune référence à un plug-in
/// PA concret (capacité <c>SupportsB2cReporting</c> — CLAUDE.md n°8) ; aucune règle fiscale inventée (catégorie/taux
/// du mapping validé F03 §2.1/§2.9, nature portée par la source).
/// </para>
/// </summary>
public sealed partial class B2cPlainTaxableReportingTenantJob : ITenantJob
{
    private const string ReadyToSendStateName = "ReadyToSend";
    private const int PageSize = 200;

    private readonly PipelineRunTrigger _trigger;

    /// <summary>Construit le job d'e-reporting B2C des documents ordinaires taxables d'un tenant.</summary>
    /// <param name="trigger">Origine du déclenchement (planifié / manuel) — tracée dans le journal d'exécutions.</param>
    public B2cPlainTaxableReportingTenantJob(PipelineRunTrigger trigger = PipelineRunTrigger.Scheduled)
    {
        _trigger = trigger;
    }

    // Motifs de blocage fail-closed (tracés, jamais un envoi à l'aveugle) d'un document ordinaire.
    private enum B2cPlainBlockReason
    {
        /// <summary>Nature d'opération Mixte ou absente : TT-81 (TLB1/TPS1) non déterminable.</summary>
        UndeterminedOperationCategory,

        /// <summary>Une ligne taxable n'a pas de taux mappé (incohérence marquage/mapping) — document non agrégé.</summary>
        LineRateUnmapped,
    }

    /// <inheritdoc />
    public string Name => "pipeline.report-b2c-plain";

    /// <inheritdoc />
    public async Task ExecuteAsync(TenantJobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var services = context.Services;
        var tenantId = context.TenantId;
        var timeProvider = services.GetRequiredService<TimeProvider>();
        var logger = services.GetRequiredService<ILogger<B2cPlainTaxableReportingTenantJob>>();
        var startedAt = timeProvider.GetUtcNow();

        var tenantSettings = services.GetRequiredService<ITenantSettingsQueries>();
        var companyId = await tenantSettings.GetCurrentCompanyId(cancellationToken);
        if (companyId is null)
        {
            // Profil tenant pas encore créé (CFG02) : rien à déclarer (transitoire).
            await WriteRunLogAsync(services, timeProvider, _trigger, startedAt, 0, 0, 0, "E-reporting B2C document ordinaire : aucun profil tenant (companyId) — rien à déclarer.", cancellationToken);
            return;
        }

        // 1) Découverte + contributions par catégorie (cœur PUR fail-closed). Aucun envoi tant que la base n'est pas résolue.
        var emissionStore = services.GetRequiredService<IB2cMarginEmissionStore>();
        var handled = await emissionStore.GetHandledDocumentIdsAsync(cancellationToken);
        var discovery = await DiscoverContributionsAsync(services, tenantId, companyId.Value, handled, logger, cancellationToken);

        // 2) Gate de capacité AVANT toute écriture d'émission : une PA sans capacité e-reporting B2C ne reçoit RIEN
        //    et AUCUN document n'est marqué tenté (repris au prochain run quand la capacité sera là).
        var paClient = await B2cReportingEmitter.ResolveActivePaClientAsync(services, tenantSettings, companyId.Value, tenantId, cancellationToken);
        if (paClient is null || !paClient.Capabilities.SupportsB2cReporting)
        {
            var pendingDetail = Describe(discovery, aggregates: 0, issued: 0, rejected: 0, technical: 0, capabilityPending: true);
            await WriteRunLogAsync(services, timeProvider, _trigger, startedAt, discovery.Examined, 0, discovery.Blocked.Count, pendingDetail, cancellationToken);
            LogCapabilityPending(logger, tenantId, discovery.Contributions.Count);
            return;
        }

        // 3) Émission (SE, F03 §2.9) : agrégation jour×devise×taux PAR catégorie (TLB1 biens, TPS1 services), un
        //    POST par TT-81. Orchestration PARTAGÉE (B2cReportingEmitter) — Pending (crash-safe) → POST → issue.
        var aggregates = 0;
        var issued = 0;
        var rejected = 0;
        var technical = 0;
        foreach (var group in discovery.Contributions.GroupBy(c => c.Category))
        {
            var transactions = B2cTaxableAggregationCalculator.Aggregate(group.Select(c => c.Contribution).ToList());
            aggregates += transactions.Count;
            var (i, r, t) = await B2cReportingEmitter.EmitAllAsync(
                services,
                emissionStore,
                paClient,
                companyId.Value,
                transactions,
                group.Key,
                EReportingDeclarantRole.Seller,
                logger,
                cancellationToken);
            issued += i;
            rejected += r;
            technical += t;
        }

        var detail = Describe(discovery, aggregates, issued, rejected, technical, capabilityPending: false);
        await WriteRunLogAsync(services, timeProvider, _trigger, startedAt, discovery.Examined, issued, discovery.Blocked.Count + rejected + technical, detail, cancellationToken);
        LogCompleted(logger, tenantId, aggregates, issued, rejected, technical);
    }

    // Découvre les documents B2C ordinaires taxables prêts (non encore tentés), lit leur pivot stagé, résout leur
    // catégorie (operationCategory) et produit UNE contribution par ligne, taguée de sa catégorie. Isolation par
    // document (un document en erreur ou bloqué n'arrête pas le run). PAS de pré-filtre frais : un document
    // ordinaire n'en porte pas (c'est précisément le discriminant — B2cPlainTaxableDeclaration).
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

        var contributions = new List<CategorizedContribution>();
        var blocked = new List<B2cPlainBlockReason>();
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
                    if (pivot is null)
                    {
                        continue; // non stagé/altéré.
                    }

                    // Marqueur 10.3 DÉRIVÉ read-time via le mapping VALIDÉ (catégorie + VATEX), comme au CHECK/SEND.
                    var marked = await B2cReportingDiscovery.EnrichForB2cMarkingAsync(tvaMapping, companyId, pivot, cancellationToken);
                    if (marked is null)
                    {
                        continue; // non mappable ce cycle (table absente / régime non couvert) — repris au suivant.
                    }

                    if (!B2cPlainTaxableDeclaration.Matches(marked))
                    {
                        continue; // bordereau d'enchères (frais) ou non marqué B2C → autre voie : pas une anomalie.
                    }

                    // TT-81 par NATURE de l'opération (F03 §2.9) : Mixte/inconnu → FAIL-CLOSED tracé (jamais deviné).
                    var category = ResolveTransactionCategory(marked.OperationCategory);
                    if (category is null)
                    {
                        blocked.Add(B2cPlainBlockReason.UndeterminedOperationCategory);
                        LogOperationCategoryUndetermined(logger, summary.Id, marked.OperationCategory?.ToString() ?? "(absente)");
                        continue;
                    }

                    examined++;
                    if (!TryBuildContributions(document, marked, category.Value, out var documentContributions, out var blockReason))
                    {
                        blocked.Add(blockReason);
                        LogPlainBlocked(logger, summary.Id, blockReason.ToString());
                        continue;
                    }

                    contributions.AddRange(documentContributions);
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

    // Construit les contributions d'UN document ordinaire (une par ligne taxable) : base HT/TVA SOURCÉE par ligne,
    // honoraire TTC = 0 (pas de spine enchères). TOUT-OU-RIEN : si une ligne n'a pas de taux mappé (incohérence
    // entre le marquage et le mapping), le DOCUMENT entier est bloqué (fail-closed tracé), jamais agrégé partiellement.
    private static bool TryBuildContributions(
        DocumentDto document,
        PivotDocumentDto marked,
        EReportingTransactionCategory category,
        out List<CategorizedContribution> contributions,
        out B2cPlainBlockReason blockReason)
    {
        contributions = new List<CategorizedContribution>(marked.Lines.Count);
        foreach (var line in marked.Lines)
        {
            // Le marquage garantit 1 ventilation taxable S/AA/AAA par ligne ; le TAUX vient du mapping validé. Taux
            // absent → incohérence → bloqué (jamais un taux deviné — n°2).
            var rate = line.Taxes[0].Rate;
            if (rate is null)
            {
                contributions.Clear();
                blockReason = B2cPlainBlockReason.LineRateUnmapped;
                return false;
            }

            contributions.Add(new CategorizedContribution(category, new B2cTaxableContribution
            {
                DocumentId = document.Id,
                SourceReference = marked.SourceReference,
                Date = document.IssueDate,
                CurrencyCode = marked.CurrencyCode,
                RatePercent = rate.Value,
                AdjudicationHt = line.NetAmount,
                AdjudicationVat = line.Taxes[0].TaxAmount,
                HonoraireTtc = 0m,
            }));
        }

        blockReason = default;
        return true;
    }

    // Résout la catégorie de transaction TT-81 (G1.68) depuis la NATURE de l'opération (F03 §2.9, posée par
    // l'agent) : livraison de biens → TLB1 (facture client) ; prestation de services → TPS1 (note d'honoraires).
    // Mixte (bien+service non ventilé par ligne) ou nature absente → null : le job BLOQUE (fail-closed tracé),
    // jamais une TT-81 devinée.
    private static EReportingTransactionCategory? ResolveTransactionCategory(OperationCategory? operationCategory) =>
        operationCategory switch
        {
            OperationCategory.LivraisonBiens => EReportingTransactionCategory.Tlb1,
            OperationCategory.PrestationServices => EReportingTransactionCategory.Tps1,
            _ => null,
        };

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
                $"E-reporting B2C document ordinaire (flux 10.3 — facture client/TLB1, note d'honoraires/TPS1) : {aggregates} agrégat(s) jour×devise EN ATTENTE de capacité PA (SupportsB2cReporting) — aucun envoi. {discovery.Examined} document(s) examiné(s), {discovery.Blocked.Count} bloqué(s) ({blocked}).");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"E-reporting B2C document ordinaire (flux 10.3 — facture client/TLB1, note d'honoraires/TPS1) : {aggregates} agrégat(s) jour×devise — {issued} transmis, {rejected} rejeté(s), {technical} erreur(s). {discovery.Examined} document(s) examiné(s), {discovery.Blocked.Count} bloqué(s) ({blocked}).");
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
        var runLog = RunLog.Start(PipelineRunType.B2cPlainTaxableAggregate, trigger, startedAt);
        runLog.Complete(
            completedAt: timeProvider.GetUtcNow(),
            documentsProcessed: processed,
            documentsSucceeded: succeeded,
            documentsFailed: failed,
            detail: detail);
        await services.GetRequiredService<IPipelineRunLogStore>().SaveAsync(runLog, cancellationToken);
    }

    [LoggerMessage(EventId = 7480, Level = LogLevel.Information,
        Message = "E-reporting B2C document ordinaire (TLB1/TPS1) terminé pour le tenant « {TenantId} » : {Aggregates} agrégat(s) — {Issued} transmis, {Rejected} rejeté(s), {Technical} erreur(s).")]
    private static partial void LogCompleted(ILogger logger, string tenantId, int aggregates, int issued, int rejected, int technical);

    [LoggerMessage(EventId = 7481, Level = LogLevel.Information,
        Message = "E-reporting B2C document ordinaire (TLB1/TPS1) pour le tenant « {TenantId} » : {Contributions} contribution(s) en attente — la PA active ne déclare pas la capacité d'e-reporting B2C (aucun envoi).")]
    private static partial void LogCapabilityPending(ILogger logger, string tenantId, int contributions);

    [LoggerMessage(EventId = 7482, Level = LogLevel.Warning,
        Message = "E-reporting B2C document ordinaire : document « {DocumentId} » bloqué (fail-closed) — motif {Reason}. Aucune transmission ; action opérateur requise.")]
    private static partial void LogPlainBlocked(ILogger logger, Guid documentId, string reason);

    [LoggerMessage(EventId = 7483, Level = LogLevel.Warning,
        Message = "E-reporting B2C document ordinaire : document « {DocumentId} » à nature d'opération indéterminée ({OperationCategory}) — la catégorie TT-81 (TLB1 bien / TPS1 service) n'est pas déterminable, document NON transmis (fail-closed, jamais deviné). Action opérateur : préciser la nature de l'opération.")]
    private static partial void LogOperationCategoryUndetermined(ILogger logger, Guid documentId, string operationCategory);

    [LoggerMessage(EventId = 7484, Level = LogLevel.Error,
        Message = "E-reporting B2C document ordinaire : échec du traitement du document « {DocumentId} » — isolé, le run continue.")]
    private static partial void LogDocumentFailed(ILogger logger, Guid documentId, Exception exception);

    // Une contribution de ligne + sa catégorie de transaction TT-81 (TLB1 bien / TPS1 service) : l'agrégation et
    // l'émission groupent par catégorie (un POST par TT-81, EmitAllAsync applique UNE catégorie par lot).
    private sealed record CategorizedContribution(EReportingTransactionCategory Category, B2cTaxableContribution Contribution);

    private sealed record DiscoveryResult(int Examined, IReadOnlyList<CategorizedContribution> Contributions, IReadOnlyList<B2cPlainBlockReason> Blocked);
}
