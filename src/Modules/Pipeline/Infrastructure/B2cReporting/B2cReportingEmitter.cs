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
using Liakont.Modules.Pipeline.Application;
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

    // Émet UN agrégat : Pending (crash-safe) AVANT le POST, puis l'issue. Si Issued, gèle le lien reporting↔pièce
    // par contribution (D2, APRÈS confirmation, clé document — export préservé). Isolé : une exception laisse les
    // entrées Pending → documents exclus du run suivant (jamais 2 POST), opérateur informé.
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
            var (status, detail) = MapResult(result);

            foreach (var contribution in transaction.Contributions)
            {
                await emissionStore.AppendAsync(
                    BuildEntry(contribution, transaction, categoryCode, roleCode, contentHash, emissionBatchId, status, result.PaDocumentId, result.RawResponse, detail),
                    cancellationToken);
            }

            if (status == B2cMarginEmissionStatus.Issued)
            {
                foreach (var contribution in transaction.Contributions)
                {
                    await FreezeReportingPieceLinkAsync(services, companyId, contribution.DocumentId, contribution.SourceReference, cancellationToken);

                    // Voie e-reporting B2C AGRÉGÉE (BUG-24, ADR-0037) : le document composant l'agrégat aboutit à
                    // EReported (ReadyToSend → EReported), à côté du gel de lien — un seul hook couvre les 4 canaux
                    // (marge/taxable/prix-total/export). BEST-EFFORT : un échec ne fait JAMAIS retomber une émission
                    // ACCEPTÉE en Technical. L'état résiduel (rare : crash/erreur de persistance dans la fenêtre) est
                    // signalé (log 7461) et réconcilié hors-ligne — il n'est PAS auto-rattrapé en régime permanent
                    // (V012 est une réconciliation one-shot, pas un job récurrent). Cf. ADR-0037 §7 + point ouvert D3.
                    await MarkDocumentEReportedAsync(services, contribution.DocumentId, emissionBatchId, logger, cancellationToken);
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

    // Transitionne le document composant l'agrégat vers EReported (BUG-24, ADR-0037) APRÈS confirmation d'envoi.
    // La transition est non-throwante côté Documents pour les cas ATTENDUS (rejeu idempotent, course) ; toute erreur
    // INATTENDUE (persistance) est ici journalisée et AVALÉE — jamais propagée — pour qu'un POST e-reporting ACCEPTÉ
    // ne retombe pas en Technical (mensonge inverse). L'état résiduel éventuel (fenêtre de crash) est signalé (log
    // 7461) et réconcilié hors-ligne — pas d'auto-rattrapage en régime permanent (V012 est one-shot). ADR-0037 §7 / D3.
    private static async Task MarkDocumentEReportedAsync(
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
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogDocumentEReportFailed(logger, documentId, ex);
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
        Message = "E-reporting B2C : transition du document {DocumentId} vers « E-reporté » échouée après un envoi ACCEPTÉ — l'émission reste valide (obligation e-reporting remplie), mais le document peut rester affiché « À envoyer ». Action : signaler au support pour réconcilier l'état persisté du document (BUG-24/ADR-0037).")]
    private static partial void LogDocumentEReportFailed(ILogger logger, Guid documentId, Exception exception);

    /// <summary>Bilan d'émission d'un lot d'agrégats (issues terminales/non terminales).</summary>
    /// <param name="Issued">Agrégats transmis (200).</param>
    /// <param name="Rejected">Agrégats rejetés par la PA.</param>
    /// <param name="Technical">Agrégats en échec technique.</param>
    internal readonly record struct EmissionTally(int Issued, int Rejected, int Technical);
}
