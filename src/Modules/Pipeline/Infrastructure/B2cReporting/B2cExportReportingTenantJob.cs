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
/// E-reporting B2C d'EXONÉRÉ INTERNATIONAL détaxé (flux 10.3, enchères, F03 §2.8) : job planifié PAR TENANT
/// (mécanique <see cref="ITenantJob"/>/<c>TenantJobRunner</c>, SOL06 ; JAMAIS une boucle multi-tenant maison).
/// Apparenté à <see cref="B2cTaxableAggregatorTenantJob"/> (même flux, même orchestration d'émission PARTAGÉE
/// <see cref="B2cReportingEmitter"/>) mais avec DEUX différences (F03 §2.8) :
/// <list type="bullet">
///   <item><b>UNITAIRE, pas agrégé</b> : une opération internationale se déclare au grain OPÉRATION — une
///   transaction e-reporting PAR document, jamais agrégée jour×devise (à la différence de la marge/prix total
///   domestiques). La traçabilité opération↔déclaration est donc 1↔1.</item>
///   <item><b>Détaxé (taux 0)</b> : l'opération est exonérée — aucune TVA. La base est le total HT exonéré
///   (adjudication HT + commission acheteur, exonérée elle aussi : le code source ne porte aucune TVA de frais sur
///   un détaxé — F03 §2.8). Un seul sous-total au taux 0.</item>
/// </list>
/// <para>
/// <b>TROIS régimes, TT-81 par catégorie</b> (résolue depuis le marquage, pas hard-codée) : export hors UE
/// (262 I, catégorie <c>G</c>) → <b>TLB1</b> ; intracommunautaire (262 ter / 258 A, catégorie <c>K</c>) →
/// <b>TNT1</b> (non soumis en France ; OSS destination = autre flux) ; franchise (275, catégorie <c>G</c>
/// export-bound) → <b>TLB1</b>. Un run mêlant les trois émet un POST par TT-81 (groupé).
/// </para>
/// <para>
/// <b>Anti-doublon</b> : MÊME journal d'émission append-only que la marge/prix total (attempt-once par document,
/// décision D3) — un document est soit marge, soit prix total, soit exonéré international, jamais plusieurs, et un
/// document déjà tenté est EXCLU des runs suivants (jamais 2 POST).
/// </para>
/// <para>
/// <b>Frontières</b> : tenant-scopé (companyId du profil tenant — CLAUDE.md n°9) ; aucune référence à un plug-in
/// PA concret (capacité <c>SupportsB2cReporting</c> — CLAUDE.md n°8) ; aucune règle fiscale inventée (catégorie
/// <c>G</c>/<c>K</c> du mapping validé F03 §2.1/§2.8, classification validée PO). La <b>commission vendeur</b> d'un
/// lot est HORS de cette base (prestation B2B) : le bordereau acheteur (BA) ne porte que la commission acheteur.
/// </para>
/// </summary>
public sealed partial class B2cExportReportingTenantJob : ITenantJob
{
    private const string ReadyToSendStateName = "ReadyToSend";
    private const int PageSize = 200;

    private readonly PipelineRunTrigger _trigger;

    /// <summary>Construit le job d'e-reporting B2C d'export hors UE d'un tenant.</summary>
    /// <param name="trigger">Origine du déclenchement (planifié / manuel) — tracée dans le journal d'exécutions.</param>
    public B2cExportReportingTenantJob(PipelineRunTrigger trigger = PipelineRunTrigger.Scheduled)
    {
        _trigger = trigger;
    }

    /// <inheritdoc />
    public string Name => "pipeline.report-b2c-export";

    /// <inheritdoc />
    public async Task ExecuteAsync(TenantJobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var services = context.Services;
        var tenantId = context.TenantId;
        var timeProvider = services.GetRequiredService<TimeProvider>();
        var logger = services.GetRequiredService<ILogger<B2cExportReportingTenantJob>>();
        var startedAt = timeProvider.GetUtcNow();

        var tenantSettings = services.GetRequiredService<ITenantSettingsQueries>();
        var companyId = await tenantSettings.GetCurrentCompanyId(cancellationToken);
        if (companyId is null)
        {
            // Profil tenant pas encore créé (CFG02) : rien à déclarer (transitoire).
            await WriteRunLogAsync(services, timeProvider, _trigger, startedAt, 0, 0, 0, "E-reporting B2C export : aucun profil tenant (companyId) — rien à déclarer.", cancellationToken);
            return;
        }

        // 1) Découverte (cœur PUR fail-closed) : une transaction UNITAIRE par document export. Aucun envoi tant
        //    que la base HT n'est pas constituée.
        var emissionStore = services.GetRequiredService<IB2cMarginEmissionStore>();
        var handled = await emissionStore.GetHandledDocumentIdsAsync(cancellationToken);
        var discovery = await DiscoverTransactionsAsync(services, tenantId, companyId.Value, handled, logger, cancellationToken);

        // 2) Gate de capacité AVANT toute écriture d'émission : une PA sans capacité e-reporting B2C ne reçoit RIEN
        //    et AUCUN document n'est marqué tenté (repris au prochain run quand la capacité sera là). L'export est
        //    de l'e-reporting B2C ORDINAIRE (catégorie TLB1) : pas de capacité « montant de marge » requise.
        var paClient = await B2cReportingEmitter.ResolveActivePaClientAsync(services, tenantSettings, companyId.Value, tenantId, cancellationToken);
        if (paClient is null || !paClient.Capabilities.SupportsB2cReporting)
        {
            var pendingDetail = Describe(discovery, issued: 0, rejected: 0, technical: 0, capabilityPending: true);
            await WriteRunLogAsync(services, timeProvider, _trigger, startedAt, discovery.Examined, 0, discovery.NotMapped, pendingDetail, cancellationToken);
            LogCapabilityPending(logger, tenantId, discovery.Transactions.Count);
            return;
        }

        // 3) Émission (SE, F03 §2.8) : orchestration PARTAGÉE (B2cReportingEmitter) — Pending (crash-safe) → POST
        //    → issue → gel des liens (D2) si Issued. Chaque transaction porte UNE seule contribution. On émet UN
        //    lot par TT-81 (TLB1 export/franchise, TNT1 intracom) : EmitAllAsync applique UNE catégorie au lot.
        var issued = 0;
        var rejected = 0;
        var technical = 0;
        foreach (var group in discovery.Transactions.GroupBy(t => t.Category))
        {
            var (i, r, t) = await B2cReportingEmitter.EmitAllAsync(
                services,
                emissionStore,
                paClient,
                companyId.Value,
                group.Select(g => g.Transaction).ToList(),
                group.Key,
                EReportingDeclarantRole.Seller,
                logger,
                cancellationToken);
            issued += i;
            rejected += r;
            technical += t;
        }

        var detail = Describe(discovery, issued, rejected, technical, capabilityPending: false);
        await WriteRunLogAsync(services, timeProvider, _trigger, startedAt, discovery.Examined, issued, discovery.NotMapped + rejected + technical, detail, cancellationToken);
        LogCompleted(logger, tenantId, discovery.Transactions.Count, issued, rejected, technical);
    }

    // Découvre les documents B2C d'export hors UE prêts (non encore tentés), lit leur pivot stagé, et produit UNE
    // transaction unitaire par document. Isolation par document (un document en erreur n'arrête pas le run).
    private static async Task<DiscoveryResult> DiscoverTransactionsAsync(
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

        var transactions = new List<TaggedTransaction>();
        var examined = 0;
        var notMapped = 0;

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

                    // Marqueur 10.3 DÉRIVÉ read-time via le mapping VALIDÉ (catégorie), comme au CHECK/SEND.
                    var marked = await B2cReportingDiscovery.EnrichForB2cMarkingAsync(tvaMapping, companyId, pivot, cancellationToken);
                    if (marked is null)
                    {
                        // Doc PORTEUR DE FRAIS dont l'adjudication n'est plus mappable depuis le CHECK : TRACÉ
                        // (jamais un skip muet). Le doc reste ReadyToSend, repris quand la table est rétablie.
                        notMapped++;
                        LogExportAdjudicationNotMapped(logger, summary.Id);
                        continue;
                    }

                    if (!B2cExportDeclaration.Matches(marked))
                    {
                        continue; // classé NON-exonéré-international (marge / prix total / acheteur pro) → autre voie.
                    }

                    examined++;
                    transactions.Add(new TaggedTransaction(
                        ResolveTransactionCategory(marked),
                        BuildExportTransaction(document, marked)));
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

        return new DiscoveryResult(examined, notMapped, transactions);
    }

    // Construit la transaction UNITAIRE d'un document export : base HT exonérée (cœur PUR
    // B2cExportBaseCalculator = Σ adjudications HT + Σ commissions acheteur, frais vendeur exclus — F03 §2.8).
    // TVA = 0, un seul sous-total au taux 0 (export détaxé, art. 262 I). Tout en decimal (CLAUDE.md n°1).
    private static B2cAggregatedTransaction BuildExportTransaction(DocumentDto document, PivotDocumentDto marked)
    {
        decimal baseHt = B2cExportBaseCalculator.ComputeTaxExclusiveBase(marked);

        var date = document.IssueDate;
        var subtotal = new B2cAggregatedSubtotal
        {
            RatePercent = 0m,
            TaxableAmount = baseHt,
            TaxTotal = 0m,
        };

        return new B2cAggregatedTransaction
        {
            Date = date,
            CurrencyCode = marked.CurrencyCode,
            TaxExclusiveAmount = baseHt,
            TaxTotal = 0m,
            Subtotals = new[] { subtotal },
            Contributions = new[]
            {
                new B2cContributionRef
                {
                    DocumentId = document.Id,
                    SourceReference = marked.SourceReference,
                },
            },
        };
    }

    // Résout la catégorie de transaction TT-81 (G1.68) depuis la catégorie UNCL5305 HOMOGÈNE du document détaxé
    // (garantie par B2cExportMarking) : intracom (K, 262 ter / 258 A) → TNT1 (non soumis en France) ; export hors
    // UE (G, 262 I) et franchise (G export-bound, 275) → TLB1 (livraison de biens détaxée). Aucune TT-81 devinée :
    // la catégorie vient de la TABLE VALIDÉE (classification validée PO, F03 §2.8).
    private static EReportingTransactionCategory ResolveTransactionCategory(PivotDocumentDto marked) =>
        B2cExportMarking.ExoneratedCategory(marked) == VatCategory.K
            ? EReportingTransactionCategory.Tnt1
            : EReportingTransactionCategory.Tlb1;

    private static string Describe(
        DiscoveryResult discovery,
        int issued,
        int rejected,
        int technical,
        bool capabilityPending)
    {
        if (capabilityPending)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"E-reporting B2C exonéré international (flux 10.3, unitaire — export 262 I/TLB1, intracom 262 ter/TNT1, franchise 275/TLB1) : {discovery.Transactions.Count} opération(s) EN ATTENTE de capacité PA (SupportsB2cReporting) — aucun envoi. {discovery.Examined} opération(s) examinée(s), {discovery.NotMapped} non mappable(s).");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"E-reporting B2C exonéré international (flux 10.3, unitaire — export 262 I/TLB1, intracom 262 ter/TNT1, franchise 275/TLB1) : {discovery.Transactions.Count} opération(s) — {issued} transmise(s), {rejected} rejetée(s), {technical} erreur(s). {discovery.Examined} opération(s) examinée(s), {discovery.NotMapped} non mappable(s).");
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
        var runLog = RunLog.Start(PipelineRunType.B2cExportReporting, trigger, startedAt);
        runLog.Complete(
            completedAt: timeProvider.GetUtcNow(),
            documentsProcessed: processed,
            documentsSucceeded: succeeded,
            documentsFailed: failed,
            detail: detail);
        await services.GetRequiredService<IPipelineRunLogStore>().SaveAsync(runLog, cancellationToken);
    }

    [LoggerMessage(EventId = 7470, Level = LogLevel.Information,
        Message = "E-reporting B2C exonéré international (unitaire) terminé pour le tenant « {TenantId} » : {Operations} opération(s) — {Issued} transmise(s), {Rejected} rejetée(s), {Technical} erreur(s).")]
    private static partial void LogCompleted(ILogger logger, string tenantId, int operations, int issued, int rejected, int technical);

    [LoggerMessage(EventId = 7471, Level = LogLevel.Information,
        Message = "E-reporting B2C exonéré international (unitaire) pour le tenant « {TenantId} » : {Operations} opération(s) en attente — la PA active ne déclare pas la capacité d'e-reporting B2C (aucun envoi).")]
    private static partial void LogCapabilityPending(ILogger logger, string tenantId, int operations);

    [LoggerMessage(EventId = 7473, Level = LogLevel.Error,
        Message = "E-reporting B2C exonéré international (unitaire) : échec du traitement du document « {DocumentId} » — isolé, le run continue.")]
    private static partial void LogDocumentFailed(ILogger logger, Guid documentId, Exception exception);

    [LoggerMessage(EventId = 7474, Level = LogLevel.Warning,
        Message = "E-reporting B2C exonéré international (unitaire) : document « {DocumentId} » porteur de frais dont l'adjudication n'est plus mappable (table de mapping TVA modifiée depuis le contrôle) — non transmis, tracé. Le document reste prêt à l'envoi ; action opérateur : faites valider/rétablir le mapping du régime de cette adjudication.")]
    private static partial void LogExportAdjudicationNotMapped(ILogger logger, Guid documentId);

    // Une transaction unitaire + sa catégorie de transaction TT-81 résolue (TLB1 export/franchise, TNT1 intracom) :
    // l'émission groupe par TT-81 (EmitAllAsync applique UNE catégorie par lot).
    private sealed record TaggedTransaction(EReportingTransactionCategory Category, B2cAggregatedTransaction Transaction);

    private sealed record DiscoveryResult(int Examined, int NotMapped, IReadOnlyList<TaggedTransaction> Transactions);
}
