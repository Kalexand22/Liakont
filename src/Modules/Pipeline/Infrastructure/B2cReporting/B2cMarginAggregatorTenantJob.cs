namespace Liakont.Modules.Pipeline.Infrastructure.B2cReporting;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Pipeline.Application;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Domain;
using Liakont.Modules.Pipeline.Domain.B2cReporting;
using Liakont.Modules.Pipeline.Infrastructure.Check;
using Liakont.Modules.Pipeline.Infrastructure.Serialization;
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

        // 1) Découverte + résolution + agrégation (cœur PUR fail-closed). Aucun envoi tant que la marge n'est pas résolue.
        var handled = await services.GetRequiredService<IB2cMarginEmissionStore>().GetHandledDocumentIdsAsync(cancellationToken);
        var discovery = await DiscoverContributionsAsync(services, tenantId, companyId.Value, handled, logger, cancellationToken);
        var transactions = B2cTransactionAggregationCalculator.Aggregate(discovery.Contributions);

        // 2) Gate de capacité AVANT toute écriture d'émission : une PA sans capacité marge ne reçoit RIEN et
        //    AUCUN document n'est marqué tenté (il reste repris au prochain run quand la capacité sera là).
        var paClient = await ResolveActivePaClientAsync(services, tenantSettings, companyId.Value, tenantId, cancellationToken);
        if (paClient is null || !paClient.Capabilities.SupportsB2cReporting || !paClient.Capabilities.SupportsMarginAmountReporting)
        {
            var pendingDetail = Describe(discovery, transactions.Count, issued: 0, rejected: 0, technical: 0, capabilityPending: true);
            await WriteRunLogAsync(services, timeProvider, _trigger, startedAt, discovery.Examined, 0, discovery.Blocked.Count, pendingDetail, cancellationToken);
            LogCapabilityPending(logger, tenantId, transactions.Count);
            return;
        }

        // 3) Émission : par agrégat, Pending (crash-safe) → POST → issue → gel des liens (D2) si Issued.
        var emissionStore = services.GetRequiredService<IB2cMarginEmissionStore>();
        var issued = 0;
        var rejected = 0;
        var technical = 0;
        foreach (var transaction in transactions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = await EmitAsync(services, emissionStore, paClient, companyId.Value, transaction, logger, cancellationToken);
            switch (status)
            {
                case B2cMarginEmissionStatus.Issued: issued++; break;
                case B2cMarginEmissionStatus.RejectedByPa: rejected++; break;
                default: technical++; break;
            }
        }

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

                    var pivot = await TryReadStagedPivotAsync(staging, tenantId, document, logger, cancellationToken);
                    if (pivot is null || !HasMarginFees(pivot))
                    {
                        continue; // non stagé/altéré, ou sans frais (jamais une marge) — pré-filtre cheap avant tout mapping.
                    }

                    // Marqueur 10.3-marge DÉRIVÉ read-time via le mapping VALIDÉ (catégorie + VATEX), comme au
                    // CHECK/SEND : le pivot stagé est le pivot SOURCE (régimes bruts, jamais marqué par l'agent).
                    // On l'enrichit ICI par le MÊME moteur (CheckTvaMapping), qui pose le marqueur — une seule
                    // source de la classification, jamais inventée (F03).
                    var marked = await EnrichForMarginMarkingAsync(tvaMapping, companyId, pivot, cancellationToken);
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
                    var resolution = await ResolveMarginAsync(tvaMapping, companyId, marked, cancellationToken);
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

    // Lit le pivot stagé d'un document. NotFound (non/plus stagé) → ignoré ce cycle (transitoire) ; Integrity
    // (contenu altéré) → ignoré + journalisé (jamais traiter un contenu altéré — CLAUDE.md n°3).
    private static async Task<PivotDocumentDto?> TryReadStagedPivotAsync(
        IPayloadStagingStore staging,
        string tenantId,
        DocumentDto document,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var key = new StagedPayloadKey(tenantId, document.Id, document.PayloadHash);
            var json = await staging.ReadAsync(key, cancellationToken);
            return PivotCanonicalJsonReader.Read(json);
        }
        catch (StagedPayloadNotFoundException)
        {
            return null;
        }
        catch (StagedPayloadIntegrityException)
        {
            LogStagingIntegrity(logger, document.Id);
            return null;
        }
    }

    // Frais de marge présents (acheteur OU vendeur) : pré-filtre cheap — seuls les documents porteurs de frais
    // peuvent être une déclaration de marge (B2cMarginDeclaration.Matches l'exige). Inutile de mapper les autres.
    private static bool HasMarginFees(PivotDocumentDto pivot) =>
        ((pivot.SellerFees?.Count ?? 0) > 0) || ((pivot.BuyerFees?.Count ?? 0) > 0);

    // Enrichit le pivot SOURCE par le mapping TVA validé (catégorie + VATEX) via le MÊME moteur qu'au CHECK/SEND
    // (CheckTvaMapping), qui DÉRIVE le marqueur de déclaration de marge B2C sur le pivot enrichi (régime marge +
    // B2C + frais + 297 E, fail-closed — voir B2cMarginMarking). Retourne null si aucune ligne mappable ou si le
    // mapping bloque (table absente / régime devenu non couvert depuis le CHECK) : document ignoré ce cycle,
    // repris au suivant — jamais marqué à l'aveugle (CLAUDE.md n°2/3).
    //
    // DETTE (assumée en build) : ce mapping de l'ADJUDICATION (Part.Autre, marquage) est distinct de celui des
    // HONORAIRES (Part.Frais) fait ensuite par ResolveMarginAsync → 2 allers-retours MapAsync par document
    // candidat (au grain frais, donc borné aux bordereaux d'enchères). Non fusionnables trivialement
    // (CheckTvaMapping.Evaluate exige Lines.Count == Requests.Count, plan adjudication-only). À revoir (MapAsync
    // multi-part unique) SI la découverte ReadyToSend devient un point chaud — pas avant (pré-filtre HasMarginFees).
    private static async Task<PivotDocumentDto?> EnrichForMarginMarkingAsync(
        ITvaMappingService tvaMapping,
        Guid companyId,
        PivotDocumentDto pivot,
        CancellationToken cancellationToken)
    {
        var plan = CheckTvaMapping.BuildPlan(pivot);
        if (plan.Requests.Count == 0)
        {
            return null; // aucune ligne de forme mappable → aucun signal de régime marge dérivable.
        }

        var mapping = await tvaMapping.MapAsync(companyId, plan.Requests, cancellationToken);
        if (!mapping.TableExists)
        {
            return null; // aucune table de mapping (supprimée depuis le CHECK) → pas de classification, jamais devinée.
        }

        var evaluation = CheckTvaMapping.Evaluate(pivot, plan, mapping);
        return evaluation.IsBlocked ? null : evaluation.EnrichedDocument;
    }

    // Résout la marge d'un document marge : somme des honoraires (TTC) à taux UNIQUE (mapping F03, Part.Frais),
    // ou blocage typé (fail-closed). Le taux vient de la table validée du tenant — jamais inventé (CLAUDE.md n°2).
    private static async Task<B2cMarginResolution> ResolveMarginAsync(
        ITvaMappingService tvaMapping,
        Guid companyId,
        PivotDocumentDto pivot,
        CancellationToken cancellationToken)
    {
        var fees = new List<(decimal AmountTtc, string? RegimeCode, string? LineRef)>();
        foreach (var fee in pivot.SellerFees ?? Enumerable.Empty<PivotSellerFeeDto>())
        {
            fees.Add((fee.NetAmount, fee.SourceRegimeCode, fee.SourceLineRef));
        }

        foreach (var fee in pivot.BuyerFees ?? Enumerable.Empty<PivotBuyerFeeDto>())
        {
            fees.Add((fee.NetAmount, fee.SourceRegimeCode, fee.SourceLineRef));
        }

        var requests = fees
            .Select(f => new TvaLineMappingRequest
            {
                SourceRegimeCode = f.RegimeCode ?? string.Empty,
                Part = TvaMappingPart.Frais,
                LineRef = f.LineRef,
            })
            .ToList();

        var mapping = await tvaMapping.MapAsync(companyId, requests, cancellationToken);

        var honoraires = new List<B2cResolvedHonoraire>(fees.Count);
        for (var i = 0; i < fees.Count; i++)
        {
            // Index 1:1 requête→résultat ; si la table est absente ou le code non mappé (ou un résultat
            // manquant), le taux reste null → B2cMarginResolver bloque (UnmappedRate), jamais un taux deviné.
            var line = mapping.TableExists && i < mapping.Lines.Count ? mapping.Lines[i] : null;
            var rate = line is { IsMapped: true } ? line.Rate : null;
            honoraires.Add(new B2cResolvedHonoraire { AmountTtc = fees[i].AmountTtc, RatePercent = rate });
        }

        return B2cMarginResolver.Resolve(HasSeparateVat(pivot), honoraires);
    }

    // Émet UN agrégat : Pending (crash-safe) AVANT le POST, puis l'issue. Si Issued, gèle le lien reporting↔pièce
    // par contribution (D2, APRÈS confirmation, clé document — export préservé). Isolé : une exception laisse les
    // entrées Pending → documents exclus du run suivant (jamais 2 POST), opérateur informé.
    private static async Task<B2cMarginEmissionStatus> EmitAsync(
        IServiceProvider services,
        IB2cMarginEmissionStore emissionStore,
        IPaClient paClient,
        Guid companyId,
        B2cAggregatedTransaction transaction,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var reportingTx = MapToReportingTransaction(transaction);
        var categoryCode = reportingTx.Category.ToTransactionCategoryCode();
        var roleCode = reportingTx.Role.ToDeclarantRoleCode();
        var contentHash = ComputeContentHash(reportingTx, categoryCode, roleCode);

        try
        {
            // Pré-POST : marque chaque document tenté (crash-safe). Un crash après le POST mais avant l'issue
            // laisse ces Pending → exclusion au run suivant (attempt-once, jamais 2 POST — D3).
            foreach (var contribution in transaction.Contributions)
            {
                await emissionStore.AppendAsync(
                    BuildEntry(contribution, transaction, categoryCode, roleCode, contentHash, B2cMarginEmissionStatus.Pending, paEmissionId: null, paResponse: null, detail: null),
                    cancellationToken);
            }

            var result = await paClient.SendB2cTransactionAsync(reportingTx, cancellationToken);
            var (status, detail) = MapResult(result);

            foreach (var contribution in transaction.Contributions)
            {
                await emissionStore.AppendAsync(
                    BuildEntry(contribution, transaction, categoryCode, roleCode, contentHash, status, result.PaDocumentId, result.RawResponse, detail),
                    cancellationToken);
            }

            if (status == B2cMarginEmissionStatus.Issued)
            {
                foreach (var contribution in transaction.Contributions)
                {
                    await FreezeReportingPieceLinkAsync(services, companyId, contribution.DocumentId, contribution.SourceReference, cancellationToken);
                }
            }

            return status;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogEmissionFailed(logger, transaction.Date, transaction.CurrencyCode, ex);
            return B2cMarginEmissionStatus.Technical;
        }
    }

    // Gèle le lien reporting↔pièce (B2C04, D2) APRÈS confirmation d'envoi : clé document conservée
    // (company_id, document_id, source_reference) → l'export fiscal GetByDocumentAsync reste fonctionnel.
    // APPEND-ONLY + idempotent (un rejeu n'insère rien). Réf source vide → aucun lien (rien d'inventé).
    private static async Task FreezeReportingPieceLinkAsync(
        IServiceProvider services,
        Guid companyId,
        Guid documentId,
        string sourceReference,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceReference))
        {
            return;
        }

        await services.GetRequiredService<IReportingPieceLinkStore>().AppendAsync(
            companyId,
            documentId,
            new[] { sourceReference },
            cancellationToken);
    }

    // Compte PA actif du tenant, ou null (aucun compte actif / plug-in non déployé) — jamais une exception dure :
    // sans capacité, le job ne transmet rien et ne marque aucun document (repris au prochain run). Aucun
    // if (pa is X) (CLAUDE.md n°8) : la décision est pilotée par les capacités déclarées.
    private static async Task<IPaClient?> ResolveActivePaClientAsync(
        IServiceProvider services,
        ITenantSettingsQueries tenantSettings,
        Guid companyId,
        string tenantId,
        CancellationToken cancellationToken)
    {
        var accounts = await tenantSettings.GetPaAccounts(companyId, cancellationToken);
        var active = accounts.FirstOrDefault(account => account.IsActive);
        if (active is null)
        {
            return null;
        }

        var registry = services.GetRequiredService<IPaClientRegistry>();
        return registry.IsRegistered(active.PluginType)
            ? registry.Resolve(new PaAccountDescriptor(active.PluginType, tenantId))
            : null;
    }

    private static B2cReportingTransaction MapToReportingTransaction(B2cAggregatedTransaction transaction) => new()
    {
        // Enchères, F03 §2.5/§2.6 : catégorie TMA1 (régime de la marge, G1.68), rôle déclarant SE (vendeur, G7.52).
        Category = EReportingTransactionCategory.Tma1,
        Role = EReportingDeclarantRole.Seller,
        CurrencyCode = transaction.CurrencyCode,
        Date = transaction.Date,
        TaxExclusiveAmount = transaction.TaxExclusiveAmount,
        TaxTotal = transaction.TaxTotal,
        Subtotals = transaction.Subtotals
            .Select(s => new B2cReportingTransactionSubtotal
            {
                TaxPercent = s.RatePercent,
                TaxableAmount = s.TaxableAmount,
                TaxTotal = s.TaxTotal,
            })
            .ToList(),
    };

    private static B2cMarginEmissionEntry BuildEntry(
        B2cContributionRef contribution,
        B2cAggregatedTransaction transaction,
        string categoryCode,
        string roleCode,
        string contentHash,
        B2cMarginEmissionStatus status,
        string? paEmissionId,
        string? paResponse,
        string? detail) => new()
        {
            DocumentId = contribution.DocumentId,
            SourceReference = contribution.SourceReference,
            AggregateDate = transaction.Date,
            CurrencyCode = transaction.CurrencyCode,
            Category = categoryCode,
            Role = roleCode,
            ContentHash = contentHash,
            Status = status,
            PaEmissionId = paEmissionId,
            PaResponseSnapshot = paResponse,
            Detail = detail,
        };

    // Issue de l'envoi → statut journal. Seul un 200 (Issued) marque le document émis ; tout le reste est non
    // terminal et signalé (jamais ré-émis en auto — l'API n'a aucun dédoublonnage, CLAUDE.md n°3).
    private static (B2cMarginEmissionStatus Status, string? Detail) MapResult(PaSendResult result)
    {
        var detail = result.Errors.Count > 0
            ? string.Join(" | ", result.Errors.Select(e => $"[{e.Code}] {e.Message}"))
            : null;

        return result.State switch
        {
            PaSendState.Issued => (B2cMarginEmissionStatus.Issued, null),
            PaSendState.RejectedByPa => (B2cMarginEmissionStatus.RejectedByPa, detail ?? "Rejet de la Plateforme Agréée."),
            _ => (B2cMarginEmissionStatus.Technical, detail ?? "Échec technique de transmission B2C (re-vérifier avant toute reprise)."),
        };
    }

    // Empreinte déterministe du contenu transmis (audit). Les sous-totaux sont déjà ordonnés par taux (calculateur).
    private static string ComputeContentHash(B2cReportingTransaction transaction, string categoryCode, string roleCode)
    {
        var builder = new StringBuilder();
        builder.Append(transaction.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Append('|').Append(transaction.CurrencyCode)
            .Append('|').Append(categoryCode)
            .Append('|').Append(roleCode)
            .Append('|').Append(transaction.TaxExclusiveAmount.ToString("0.00", CultureInfo.InvariantCulture))
            .Append('|').Append(transaction.TaxTotal.ToString("0.00", CultureInfo.InvariantCulture));
        foreach (var subtotal in transaction.Subtotals)
        {
            builder.Append('|').Append(subtotal.TaxPercent.ToString("0.0###", CultureInfo.InvariantCulture))
                .Append(':').Append(subtotal.TaxableAmount.ToString("0.00", CultureInfo.InvariantCulture))
                .Append(':').Append(subtotal.TaxTotal.ToString("0.00", CultureInfo.InvariantCulture));
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    // Garde 297 E (miroir de MarginCalculator.EnsureNoSeparateVat) : le montant de marge est une BASE — aucune
    // TVA distincte. Total de TVA non nul, ou une ligne portant une ventilation de TVA > 0 → marge non séparable.
    private static bool HasSeparateVat(PivotDocumentDto pivot) =>
        pivot.Totals.TotalTax != 0m || pivot.Lines.Any(line => line.Taxes.Any(tax => tax.TaxAmount != 0m));

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

    [LoggerMessage(EventId = 7444, Level = LogLevel.Warning,
        Message = "E-reporting B2C marge : pivot stagé altéré pour le document « {DocumentId} » — ignoré (jamais traiter un contenu altéré).")]
    private static partial void LogStagingIntegrity(ILogger logger, Guid documentId);

    [LoggerMessage(EventId = 7445, Level = LogLevel.Error,
        Message = "E-reporting B2C marge : échec d'émission de l'agrégat du {Date} ({Currency}) — entrées Pending conservées (exclues du run suivant, jamais 2 POST).")]
    private static partial void LogEmissionFailed(ILogger logger, DateOnly date, string currency, Exception exception);

    [LoggerMessage(EventId = 7446, Level = LogLevel.Warning,
        Message = "E-reporting B2C marge : document « {DocumentId} » porteur de frais dont l'adjudication n'est plus mappable (table de mapping TVA modifiée depuis le contrôle) — non agrégé, tracé. Le document reste prêt à l'envoi ; action opérateur : faites valider/rétablir le mapping du régime de cette adjudication.")]
    private static partial void LogMarginAdjudicationNotMapped(ILogger logger, Guid documentId);

    private sealed record DiscoveryResult(int Examined, IReadOnlyList<B2cMarginContribution> Contributions, IReadOnlyList<B2cMarginBlockReason> Blocked);
}
