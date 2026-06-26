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
/// E-reporting B2C au RÉGIME DU PRIX TOTAL taxable (flux 10.3, enchères — TLB1, BUG-8) : job planifié PAR TENANT
/// (mécanique <see cref="ITenantJob"/>/<c>TenantJobRunner</c>, SOL06 ; JAMAIS une boucle multi-tenant maison).
/// SYMÉTRIQUE de <see cref="B2cMarginAggregatorTenantJob"/> (marge, TMA1) : même flux, même orchestration
/// d'émission PARTAGÉE (<see cref="B2cReportingEmitter"/>), mais la base est le <b>prix total payé</b>
/// (adjudication taxable HT/TVA SOURCÉE + commission acheteur TTC ramenée HT — F03 §2.7/§270) et la catégorie de
/// transaction est <b>TLB1</b> (livraison de biens, G1.68). Pour le tenant courant : découvre les documents B2C
/// taxables prêts (marqueur <c>IsB2cReportingDeclaration</c> + TVA distincte + frais acheteur), résout leur base
/// (cœur PUR <see cref="B2cTaxableResolver"/>, fail-closed), agrège jour×devise×taux
/// (<see cref="B2cTaxableAggregationCalculator"/>), puis TRANSMET chaque agrégat à la PA active.
/// <para>
/// <b>Anti-doublon</b> : MÊME journal d'émission append-only que la marge (attempt-once par document, décision
/// D3) — un document est soit marge (<c>TotalTax == 0</c>) soit taxable (<c>TotalTax &gt; 0</c>), jamais les deux,
/// et un document déjà tenté est EXCLU des runs suivants (jamais 2 POST).
/// </para>
/// <para>
/// <b>Frontières</b> : tenant-scopé (companyId du profil tenant — CLAUDE.md n°9) ; aucune référence à un plug-in
/// PA concret (capacité <c>SupportsB2cReporting</c> — CLAUDE.md n°8) ; aucune règle fiscale inventée (taux du
/// mapping F03, forme ancrée F03 §2.7). La <b>commission vendeur</b> d'un lot taxable est HORS de cette base
/// (prestation B2B, F03 §2.7) : le bordereau acheteur (BA) ne porte que la commission acheteur.
/// </para>
/// </summary>
public sealed partial class B2cTaxableAggregatorTenantJob : ITenantJob
{
    private const string ReadyToSendStateName = "ReadyToSend";
    private const int PageSize = 200;

    private readonly PipelineRunTrigger _trigger;

    /// <summary>Construit le job d'e-reporting B2C au régime du prix total d'un tenant.</summary>
    /// <param name="trigger">Origine du déclenchement (planifié / manuel) — tracée dans le journal d'exécutions.</param>
    public B2cTaxableAggregatorTenantJob(PipelineRunTrigger trigger = PipelineRunTrigger.Scheduled)
    {
        _trigger = trigger;
    }

    /// <inheritdoc />
    public string Name => "pipeline.aggregate-b2c-taxable";

    /// <inheritdoc />
    public async Task ExecuteAsync(TenantJobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var services = context.Services;
        var tenantId = context.TenantId;
        var timeProvider = services.GetRequiredService<TimeProvider>();
        var logger = services.GetRequiredService<ILogger<B2cTaxableAggregatorTenantJob>>();
        var startedAt = timeProvider.GetUtcNow();

        var tenantSettings = services.GetRequiredService<ITenantSettingsQueries>();
        var companyId = await tenantSettings.GetCurrentCompanyId(cancellationToken);
        if (companyId is null)
        {
            // Profil tenant pas encore créé (CFG02) : rien à déclarer (transitoire).
            await WriteRunLogAsync(services, timeProvider, _trigger, startedAt, 0, 0, 0, "E-reporting B2C taxable : aucun profil tenant (companyId) — rien à déclarer.", cancellationToken);
            return;
        }

        // 1) Découverte + résolution + agrégation (cœur PUR fail-closed). Aucun envoi tant que la base n'est pas résolue.
        var emissionStore = services.GetRequiredService<IB2cMarginEmissionStore>();
        var handled = await emissionStore.GetHandledDocumentIdsAsync(cancellationToken);
        var discovery = await DiscoverContributionsAsync(services, tenantId, companyId.Value, handled, logger, cancellationToken);
        var transactions = B2cTaxableAggregationCalculator.Aggregate(discovery.Contributions);

        // 2) Gate de capacité AVANT toute écriture d'émission : une PA sans capacité e-reporting B2C ne reçoit RIEN
        //    et AUCUN document n'est marqué tenté (repris au prochain run quand la capacité sera là). TLB1 est de
        //    l'e-reporting B2C ORDINAIRE (pas de capacité « montant de marge » — celle-ci est propre à TMA1).
        var paClient = await B2cReportingEmitter.ResolveActivePaClientAsync(services, tenantSettings, companyId.Value, tenantId, cancellationToken);
        if (paClient is null || !paClient.Capabilities.SupportsB2cReporting)
        {
            var pendingDetail = Describe(discovery, transactions.Count, issued: 0, rejected: 0, technical: 0, capabilityPending: true);
            await WriteRunLogAsync(services, timeProvider, _trigger, startedAt, discovery.Examined, 0, discovery.Blocked.Count, pendingDetail, cancellationToken);
            LogCapabilityPending(logger, tenantId, transactions.Count);
            return;
        }

        // 3) Émission (TLB1 / SE, F03 §2.7) : orchestration PARTAGÉE (B2cReportingEmitter) ; seuls catégorie/rôle
        //    distinguent le prix total — Pending (crash-safe) → POST → issue → gel des liens (D2) si Issued.
        var (issued, rejected, technical) = await B2cReportingEmitter.EmitAllAsync(
            services,
            emissionStore,
            paClient,
            companyId.Value,
            transactions,
            EReportingTransactionCategory.Tlb1,
            EReportingDeclarantRole.Seller,
            logger,
            cancellationToken);

        var detail = Describe(discovery, transactions.Count, issued, rejected, technical, capabilityPending: false);
        await WriteRunLogAsync(services, timeProvider, _trigger, startedAt, discovery.Examined, issued, discovery.Blocked.Count + rejected + technical, detail, cancellationToken);
        LogCompleted(logger, tenantId, transactions.Count, issued, rejected, technical);
    }

    // Découvre les documents B2C taxables prêts (non encore tentés), lit leur pivot stagé, résout la base, et
    // produit les contributions. Isolation par document (un document en erreur n'arrête pas le run).
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

        var contributions = new List<B2cTaxableContribution>();
        var blocked = new List<B2cTaxableBlockReason>();
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
                        continue; // non stagé/altéré, ou sans frais — pré-filtre cheap avant tout mapping.
                    }

                    // Marqueur 10.3 DÉRIVÉ read-time via le mapping VALIDÉ (catégorie + VATEX), comme au CHECK/SEND.
                    var marked = await B2cReportingDiscovery.EnrichForB2cMarkingAsync(tvaMapping, companyId, pivot, cancellationToken);
                    if (marked is null)
                    {
                        // Doc PORTEUR DE FRAIS dont l'adjudication n'est plus mappable depuis le CHECK : TRACÉ
                        // (jamais un skip muet). Le doc reste ReadyToSend, repris quand la table est rétablie.
                        blocked.Add(B2cTaxableBlockReason.AdjudicationNotMapped);
                        LogTaxableAdjudicationNotMapped(logger, summary.Id);
                        continue;
                    }

                    if (!B2cTaxableDeclaration.Matches(marked))
                    {
                        continue; // classé NON-taxable (marge / acheteur pro) → autre voie : pas une anomalie, pas tracé.
                    }

                    examined++;
                    var resolution = await ResolveTaxableAsync(tvaMapping, companyId, marked, cancellationToken);
                    if (resolution.IsResolved)
                    {
                        foreach (var component in resolution.Components!)
                        {
                            contributions.Add(new B2cTaxableContribution
                            {
                                DocumentId = document.Id,
                                SourceReference = pivot.SourceReference,
                                Date = document.IssueDate,
                                CurrencyCode = pivot.CurrencyCode,
                                RatePercent = component.RatePercent,
                                AdjudicationHt = component.AdjudicationHt,
                                AdjudicationVat = component.AdjudicationVat,
                                HonoraireTtc = component.HonoraireTtc,
                            });
                        }
                    }
                    else if (resolution.BlockReason is { } reason)
                    {
                        blocked.Add(reason);
                        LogTaxableBlocked(logger, summary.Id, reason.ToString());
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

    // Résout la base taxable d'un document : adjudication (HT/TVA SOURCÉES des lignes enrichies) + commission
    // ACHETEUR (TTC, taux mappé F03 Part.Frais), regroupées par taux (cœur PUR fail-closed). La commission
    // vendeur est EXCLUE (prestation B2B, F03 §2.7) : le BA ne porte que des frais acheteur.
    private static async Task<B2cTaxableResolution> ResolveTaxableAsync(
        ITvaMappingService tvaMapping,
        Guid companyId,
        PivotDocumentDto pivot,
        CancellationToken cancellationToken)
    {
        // Adjudication : HT/TVA et taux lus sur les lignes ENRICHIES (le marquage garantit 1 ventilation taxable
        // par ligne) — montants sourcés, jamais recalculés. Partition par RÔLE (BUG-17 volet b) : on EXCLUT les
        // lignes d'honoraire acheteur (rôle BuyerFee), sinon la commission serait comptée DEUX fois (ligne + ci-dessous).
        var adjudicationLines = B2cAuctionFeeLines.AdjudicationLines(pivot)
            .Select(line => new B2cTaxableLineAmount
            {
                RatePercent = line.Taxes[0].Rate,
                TaxableHt = line.NetAmount,
                TaxVat = line.Taxes[0].TaxAmount,
            })
            .ToList();

        // Commission ACHETEUR (F03 §2.7) : lue sur les LIGNES au rôle BuyerFee (BUG-17 volet b) → taux mappé Part.Frais
        // (jamais inventé, fail-closed). Montant TTC recouvré PAR CONSTRUCTION = NetAmount + TVA de ligne. Aujourd'hui la
        // ligne honoraire reste TTC-pliée (taxe de ligne 0) sous TOUT régime — le dé-pliage HT/TVA d'un honoraire au
        // régime du PRIX TOTAL (facture B2B Factur-X) est DÉFÉRÉ (BUG-17, suivi séparé) ; le « +TVA de ligne » garde la
        // formule robuste si ce dé-pliage est ajouté plus tard. Le taux de la commission vient ici du mapping Part.Frais.
        var buyerFeeLines = B2cAuctionFeeLines.BuyerFeeLines(pivot).ToList();
        var requests = buyerFeeLines
            .Select(line => new TvaLineMappingRequest
            {
                SourceRegimeCode = line.SourceRegimeCodes.Count > 0 ? line.SourceRegimeCodes[0] : string.Empty,
                Part = TvaMappingPart.Frais,
                LineRef = line.SourceLineRef,
            })
            .ToList();

        var mapping = requests.Count > 0
            ? await tvaMapping.MapAsync(companyId, requests, cancellationToken)
            : null;

        var honoraires = new List<B2cResolvedHonoraire>(buyerFeeLines.Count);
        for (var i = 0; i < buyerFeeLines.Count; i++)
        {
            // Index 1:1 requête→résultat ; table absente / code non mappé → taux null → B2cTaxableResolver bloque
            // (UnmappedRate), jamais un taux deviné.
            var line = mapping is { TableExists: true } && i < mapping.Lines.Count ? mapping.Lines[i] : null;
            var rate = line is { IsMapped: true } ? line.Rate : null;
            decimal amountTtc = buyerFeeLines[i].NetAmount + buyerFeeLines[i].Taxes.Sum(t => t.TaxAmount);
            honoraires.Add(new B2cResolvedHonoraire { AmountTtc = amountTtc, RatePercent = rate });
        }

        return B2cTaxableResolver.Resolve(adjudicationLines, honoraires);
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
                $"E-reporting B2C prix total (flux 10.3, TLB1) : {aggregates} agrégat(s) jour×devise EN ATTENTE de capacité PA (SupportsB2cReporting) — aucun envoi. {discovery.Examined} document(s) taxable(s) examiné(s), {discovery.Blocked.Count} bloqué(s) ({blocked}).");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"E-reporting B2C prix total (flux 10.3, TLB1) : {aggregates} agrégat(s) jour×devise — {issued} transmis, {rejected} rejeté(s), {technical} erreur(s). {discovery.Examined} document(s) taxable(s) examiné(s), {discovery.Blocked.Count} bloqué(s) ({blocked}).");
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
        var runLog = RunLog.Start(PipelineRunType.B2cTaxableAggregate, trigger, startedAt);
        runLog.Complete(
            completedAt: timeProvider.GetUtcNow(),
            documentsProcessed: processed,
            documentsSucceeded: succeeded,
            documentsFailed: failed,
            detail: detail);
        await services.GetRequiredService<IPipelineRunLogStore>().SaveAsync(runLog, cancellationToken);
    }

    [LoggerMessage(EventId = 7450, Level = LogLevel.Information,
        Message = "E-reporting B2C prix total (TLB1) terminé pour le tenant « {TenantId} » : {Aggregates} agrégat(s) — {Issued} transmis, {Rejected} rejeté(s), {Technical} erreur(s).")]
    private static partial void LogCompleted(ILogger logger, string tenantId, int aggregates, int issued, int rejected, int technical);

    [LoggerMessage(EventId = 7451, Level = LogLevel.Information,
        Message = "E-reporting B2C prix total (TLB1) pour le tenant « {TenantId} » : {Aggregates} agrégat(s) en attente — la PA active ne déclare pas la capacité d'e-reporting B2C (aucun envoi).")]
    private static partial void LogCapabilityPending(ILogger logger, string tenantId, int aggregates);

    [LoggerMessage(EventId = 7452, Level = LogLevel.Warning,
        Message = "E-reporting B2C prix total (TLB1) : document « {DocumentId} » bloqué (fail-closed) — motif {Reason}. Aucune transmission ; action opérateur requise.")]
    private static partial void LogTaxableBlocked(ILogger logger, Guid documentId, string reason);

    [LoggerMessage(EventId = 7453, Level = LogLevel.Error,
        Message = "E-reporting B2C prix total (TLB1) : échec du traitement du document « {DocumentId} » — isolé, le run continue.")]
    private static partial void LogDocumentFailed(ILogger logger, Guid documentId, Exception exception);

    [LoggerMessage(EventId = 7454, Level = LogLevel.Warning,
        Message = "E-reporting B2C prix total (TLB1) : document « {DocumentId} » porteur de frais dont l'adjudication n'est plus mappable (table de mapping TVA modifiée depuis le contrôle) — non agrégé, tracé. Le document reste prêt à l'envoi ; action opérateur : faites valider/rétablir le mapping du régime de cette adjudication.")]
    private static partial void LogTaxableAdjudicationNotMapped(ILogger logger, Guid documentId);

    private sealed record DiscoveryResult(int Examined, IReadOnlyList<B2cTaxableContribution> Contributions, IReadOnlyList<B2cTaxableBlockReason> Blocked);
}
