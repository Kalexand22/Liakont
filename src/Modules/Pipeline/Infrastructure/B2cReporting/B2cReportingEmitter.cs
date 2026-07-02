namespace Liakont.Modules.Pipeline.Infrastructure.B2cReporting;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Documents.Contracts.Lifecycle;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Pipeline.Application;
using Liakont.Modules.Pipeline.Contracts.Queries;
using Liakont.Modules.Pipeline.Domain.B2cReporting;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.Transmission.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Émission PARTAGÉE des transactions e-reporting B2C agrégées (flux 10.3) — cœur d'infrastructure commun aux
/// DEUX régimes : la MARGE (<see cref="B2cMarginAggregatorTenantJob"/>, TMA1) et le PRIX TOTAL taxable
/// (<see cref="B2cTaxableAggregatorTenantJob"/>, TLB1). La catégorie de transaction (TT-81) et le rôle (TT-15)
/// sont des PARAMÈTRES de l'appelant (jamais hard-codés ici) ; seule l'orchestration d'envoi est partagée :
/// résolution de la PA active, <b>anti-doublon attempt-once par document</b> (entrée
/// <see cref="B2cMarginEmissionStatus.Pending"/> AVANT le POST, crash-safe — l'API SuperPDP n'a aucune clé
/// d'idempotence, décision D3), POST, journalisation d'issue, et gel du lien reporting↔pièce (D2) après
/// confirmation. Tenant-scopé (companyId, connexion routée — CLAUDE.md n°9) ; aucune référence à un plug-in PA
/// concret (capacité résolue via <see cref="IPaClientRegistry"/> — CLAUDE.md n°8) ; aucune règle fiscale
/// (montants déjà agrégés en amont, decimal — CLAUDE.md n°1/2).
/// </summary>
internal static partial class B2cReportingEmitter
{
    // État de découverte des documents e-reportables (les 4 jobs B2C les prennent en ReadyToSend).
    private const string ReadyToSendStateName = "ReadyToSend";

    // Pagination du rattrapage (parité PageSize des jobs d'agrégation) : borne la lecture ReadyToSend.
    private const int ReconcilePageSize = 200;

    /// <summary>
    /// Compte PA actif du tenant, ou <c>null</c> (aucun compte actif / plug-in non déployé) — jamais une
    /// exception dure : sans capacité, le job ne transmet rien et ne marque aucun document (repris au prochain
    /// run). Aucun <c>if (pa is X)</c> (CLAUDE.md n°8) : la décision est pilotée par les capacités déclarées.
    /// </summary>
    /// <param name="services">Le fournisseur de services du scope tenant.</param>
    /// <param name="tenantSettings">Les requêtes de paramétrage du tenant.</param>
    /// <param name="companyId">L'identité fiscale du tenant.</param>
    /// <param name="tenantId">Le tenant courant (descripteur de compte PA).</param>
    /// <param name="cancellationToken">Le jeton d'annulation.</param>
    /// <returns>Le client PA actif, ou <c>null</c>.</returns>
    public static async Task<IPaClient?> ResolveActivePaClientAsync(
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

    /// <summary>
    /// Émet chaque agrégat sous la <paramref name="category"/> / <paramref name="role"/> fournies. Isolé par
    /// agrégat (une exception laisse les entrées Pending → documents exclus du run suivant, jamais 2 POST).
    /// </summary>
    /// <param name="services">Le fournisseur de services du scope tenant.</param>
    /// <param name="emissionStore">Le journal d'émission append-only (anti-doublon attempt-once).</param>
    /// <param name="paClient">La PA active (capacité déjà gardée par l'appelant).</param>
    /// <param name="companyId">L'identité fiscale du tenant.</param>
    /// <param name="transactions">Les agrégats jour×devise×taux à transmettre.</param>
    /// <param name="category">Catégorie de transaction TT-81 (TMA1 marge / TLB1 prix total).</param>
    /// <param name="role">Rôle du déclarant TT-15 (SE pour les ventes).</param>
    /// <param name="logger">Le journal applicatif.</param>
    /// <param name="cancellationToken">Le jeton d'annulation.</param>
    /// <returns>Le bilan d'émission.</returns>
    public static async Task<EmissionTally> EmitAllAsync(
        IServiceProvider services,
        IB2cMarginEmissionStore emissionStore,
        IPaClient paClient,
        Guid companyId,
        IReadOnlyList<B2cAggregatedTransaction> transactions,
        EReportingTransactionCategory category,
        EReportingDeclarantRole role,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var issued = 0;
        var rejected = 0;
        var technical = 0;
        foreach (var transaction in transactions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = await EmitOneAsync(services, emissionStore, paClient, companyId, transaction, category, role, logger, cancellationToken);
            switch (status)
            {
                case B2cMarginEmissionStatus.Issued: issued++; break;
                case B2cMarginEmissionStatus.RejectedByPa: rejected++; break;
                default: technical++; break;
            }
        }

        return new EmissionTally(issued, rejected, technical);
    }

    /// <summary>
    /// Rattrapage EN RÉGIME PERMANENT (ADR-0037 D3) de l'état résiduel « émission ACCEPTÉE (entrée journal Issued)
    /// mais document resté <c>ReadyToSend</c> » — créé par une fenêtre de crash/annulation entre le POST accepté et
    /// la transition d'état (le document est alors exclu de tout run par l'attempt-once, donc affiché « À envoyer »
    /// indéfiniment). Rejoue la SEULE transition <c>ReadyToSend → EReported</c> (idempotente, non-throwante),
    /// JAMAIS un re-POST : les documents concernés portent déjà une entrée journal Issued. AGNOSTIQUE AU CANAL
    /// (marge / prix total / export / ordinaire — la table <c>b2c_margin_emissions</c> est partagée par les 4 voies).
    /// Tenant-scopé (services du scope tenant). Le gel du lien reporting↔pièce n'est PAS rejoué ici (D3 = état
    /// seulement) — il est déjà idempotent au run nominal. Porté par le job marge, qui tourne pour CHAQUE tenant à
    /// chaque cadence (fan-out) : ce filet couvre les résidus des 4 voies sans dupliquer la logique par canal.
    /// </summary>
    /// <param name="services">Le fournisseur de services du scope tenant.</param>
    /// <param name="handled">Documents DÉJÀ TENTÉS (attempt-once) : pré-filtre — seul un document tenté peut être résiduel.</param>
    /// <param name="logger">Le journal applicatif.</param>
    /// <param name="cancellationToken">Le jeton d'annulation.</param>
    /// <returns>Le nombre de documents réconciliés (transition rejouée avec succès).</returns>
    public static async Task<int> ReconcileResidualEReportsAsync(
        IServiceProvider services,
        IReadOnlySet<Guid> handled,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (handled.Count == 0)
        {
            return 0; // rien n'a jamais été tenté → aucun résiduel possible.
        }

        var documents = services.GetRequiredService<IDocumentQueries>();
        var emissionQueries = services.GetRequiredService<IB2cMarginEmissionQueries>();
        var reconciled = 0;

        for (var page = 1; ; page++)
        {
            var summaries = await documents.GetByStateAsync(ReadyToSendStateName, page, ReconcilePageSize, cancellationToken);
            if (summaries.Count == 0)
            {
                break;
            }

            foreach (var summary in summaries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!handled.Contains(summary.Id))
                {
                    continue; // jamais tenté → pas un résiduel (émis par la découverte du run).
                }

                // Tenté ET encore ReadyToSend : n'est un résiduel E-REPORTÉ que s'il porte une émission Issued.
                // Un document seulement Pending (crash avant l'issue) ou RejectedByPa/Technical (D1) n'est PAS
                // e-reporté → GetEmissionBatchIdForDocumentAsync rend null → on le laisse tel quel (jamais un
                // faux EReported).
                var emissionBatchId = await emissionQueries.GetEmissionBatchIdForDocumentAsync(summary.Id, cancellationToken);
                if (emissionBatchId is null)
                {
                    continue;
                }

                if (await TryMarkDocumentEReportedAsync(services, summary.Id, emissionBatchId.Value, logger, cancellationToken))
                {
                    reconciled++;
                }
            }

            if (summaries.Count < ReconcilePageSize)
            {
                break;
            }
        }

        if (reconciled > 0)
        {
            LogResidualReconciled(logger, reconciled);
        }

        return reconciled;
    }

    // Émet UN agrégat : Pending (crash-safe) AVANT le POST, puis l'issue. Une exception DANS la voie d'émission
    // (Pending/POST/issue) laisse les entrées Pending → document exclu du run suivant (attempt-once, jamais 2 POST),
    // opérateur informé (7460). Si Issued, la finalisation (gel du lien D2 + transition EReported ADR-0037) se fait
    // HORS de ce try d'émission, résiliente PAR CONTRIBUTION : elle ne peut plus faire retomber une émission ACCEPTÉE
    // en Technical ni logger un 7460 mensonger ; le résiduel éventuel est rattrapé au run suivant (D3).
    private static async Task<B2cMarginEmissionStatus> EmitOneAsync(
        IServiceProvider services,
        IB2cMarginEmissionStore emissionStore,
        IPaClient paClient,
        Guid companyId,
        B2cAggregatedTransaction transaction,
        EReportingTransactionCategory category,
        EReportingDeclarantRole role,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var reportingTx = MapToReportingTransaction(transaction, category, role);
        var categoryCode = reportingTx.Category.ToTransactionCategoryCode();
        var roleCode = reportingTx.Role.ToDeclarantRoleCode();
        var contentHash = ComputeContentHash(reportingTx, categoryCode, roleCode);

        // Identité de CETTE transmission (lot d'émission) : une valeur par POST, partagée par les entrées Pending
        // (avant le POST) et d'issue (après). Distingue deux transmissions d'un MÊME contenu (content_hash
        // identique — document tardif → nouvel agrégat) à la vue console, jamais fusionnées.
        var emissionBatchId = Guid.NewGuid();

        B2cMarginEmissionStatus status;
        try
        {
            // Pré-POST : marque chaque document tenté (crash-safe). Un crash après le POST mais avant l'issue
            // laisse ces Pending → exclusion au run suivant (attempt-once, jamais 2 POST — D3).
            foreach (var contribution in transaction.Contributions)
            {
                await emissionStore.AppendAsync(
                    BuildEntry(contribution, transaction, categoryCode, roleCode, contentHash, emissionBatchId, B2cMarginEmissionStatus.Pending, paEmissionId: null, paResponse: null, detail: null),
                    cancellationToken);
            }

            var result = await paClient.SendB2cTransactionAsync(reportingTx, cancellationToken);
            string? detail;
            (status, detail) = MapResult(result);

            foreach (var contribution in transaction.Contributions)
            {
                await emissionStore.AppendAsync(
                    BuildEntry(contribution, transaction, categoryCode, roleCode, contentHash, emissionBatchId, status, result.PaDocumentId, result.RawResponse, detail),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Échec de l'ÉMISSION elle-même (écriture Pending, POST, ou écriture d'issue) : les entrées Pending
            // écrites subsistent → le document est exclu du run suivant (attempt-once, jamais 2 POST). Le message
            // 7460 est EXACT ici (on est bien dans la voie « émission en échec », pas dans la finalisation post-POST).
            LogEmissionFailed(logger, transaction.Date, transaction.CurrencyCode, ex);
            return B2cMarginEmissionStatus.Technical;
        }

        // Finalisation post-ACCEPTATION, DÉLIBÉRÉMENT HORS du try d'émission : le POST est confirmé et le journal
        // d'issue est écrit → le statut retourné est FIGÉ (Issued). Le gel de lien (D2) et la transition d'état
        // (EReported, ADR-0037) sont BEST-EFFORT et RÉSILIENTS PAR CONTRIBUTION : un échec (persistance) ou une
        // annulation (shutdown) sur une contribution est journalisé (7461) et n'interrompt NI les autres
        // contributions NI l'émission — jamais un retour Technical (mensonge inverse), jamais le 7460 mensonger
        // (les entrées sont Issued, pas Pending). L'état résiduel « journal Issued / document ReadyToSend » est
        // rattrapé en régime permanent par ReconcileResidualEReportsAsync au run suivant (ADR-0037 D3).
        if (status == B2cMarginEmissionStatus.Issued)
        {
            foreach (var contribution in transaction.Contributions)
            {
                await FinalizeAcceptedContributionAsync(services, companyId, contribution, emissionBatchId, logger, cancellationToken);
            }
        }

        return status;
    }

    // Finalise UNE contribution d'un agrégat ACCEPTÉ (BUG-24, ADR-0037) : gel du lien reporting↔pièce (D2, clé
    // document — export préservé) PUIS transition ReadyToSend → EReported, un seul hook pour les 4 canaux
    // (marge/taxable/prix-total/export). Résilient : toute erreur (persistance) OU annulation (shutdown) est
    // journalisée (7461) et AVALÉE — jamais propagée — pour qu'un POST e-reporting ACCEPTÉ ne retombe pas en
    // Technical et que les autres contributions soient finalisées. Le résiduel éventuel est rattrapé au run
    // suivant (ReconcileResidualEReportsAsync, ADR-0037 D3). MarkEReportedAsync est idempotent/atomique côté
    // Documents (rejeu = no-op réussi ; hors ReadyToSend = pas de transition, pas de throw).
    private static async Task FinalizeAcceptedContributionAsync(
        IServiceProvider services,
        Guid companyId,
        B2cContributionRef contribution,
        Guid emissionBatchId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            await FreezeReportingPieceLinkAsync(services, companyId, contribution.DocumentId, contribution.SourceReference, cancellationToken);
            await services.GetRequiredService<IDocumentLifecycle>()
                .MarkEReportedAsync(contribution.DocumentId, emissionBatchId, cancellationToken);
        }
        catch (Exception ex)
        {
            LogDocumentEReportFailed(logger, contribution.DocumentId, ex);
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

    // Transitionne un document vers EReported (BUG-24, ADR-0037) de façon RÉSILIENTE : la transition est
    // non-throwante côté Documents pour les cas ATTENDUS (rejeu idempotent, course) ; toute erreur INATTENDUE
    // (persistance) est journalisée (7461) et AVALÉE — jamais propagée — pour ne pas interrompre la boucle de
    // rattrapage. L'annulation coopérative (shutdown) est re-propagée pour arrêter proprement le rattrapage.
    // Retourne true si la transition a été jouée sans erreur. Réutilisé par le rattrapage (ADR-0037 D3).
    private static async Task<bool> TryMarkDocumentEReportedAsync(
        IServiceProvider services,
        Guid documentId,
        Guid emissionBatchId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            await services.GetRequiredService<IDocumentLifecycle>()
                .MarkEReportedAsync(documentId, emissionBatchId, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogDocumentEReportFailed(logger, documentId, ex);
            return false;
        }
    }

    private static B2cReportingTransaction MapToReportingTransaction(
        B2cAggregatedTransaction transaction,
        EReportingTransactionCategory category,
        EReportingDeclarantRole role) => new()
        {
            Category = category,
            Role = role,
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
        Guid emissionBatchId,
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
            EmissionBatchId = emissionBatchId,
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

    [LoggerMessage(EventId = 7460, Level = LogLevel.Error,
        Message = "E-reporting B2C : échec d'émission de l'agrégat du {Date} ({Currency}) — entrées Pending conservées (exclues du run suivant, jamais 2 POST).")]
    private static partial void LogEmissionFailed(ILogger logger, DateOnly date, string currency, Exception exception);

    [LoggerMessage(EventId = 7461, Level = LogLevel.Warning,
        Message = "E-reporting B2C : transition du document {DocumentId} vers « E-reporté » échouée après un envoi ACCEPTÉ — l'émission reste valide (obligation e-reporting remplie). Le document peut rester affiché « À envoyer » jusqu'au rattrapage AUTOMATIQUE du prochain run e-reporting (ADR-0037 D3). Action seulement si l'écart PERSISTE sur plusieurs runs : signaler au support (erreur de persistance récurrente).")]
    private static partial void LogDocumentEReportFailed(ILogger logger, Guid documentId, Exception exception);

    [LoggerMessage(EventId = 7462, Level = LogLevel.Information,
        Message = "E-reporting B2C : {Count} document(s) au statut résiduel « déclaré mais resté À envoyer » rattrapé(s) automatiquement (transition « E-reporté » rejouée, sans nouvelle transmission — ADR-0037 D3).")]
    private static partial void LogResidualReconciled(ILogger logger, int count);

    /// <summary>Bilan d'émission d'un lot d'agrégats (issues terminales/non terminales).</summary>
    /// <param name="Issued">Agrégats transmis (200).</param>
    /// <param name="Rejected">Agrégats rejetés par la PA.</param>
    /// <param name="Technical">Agrégats en échec technique.</param>
    internal readonly record struct EmissionTally(int Issued, int Rejected, int Technical);
}
