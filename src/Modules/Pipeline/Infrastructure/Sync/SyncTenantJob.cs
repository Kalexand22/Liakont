namespace Liakont.Modules.Pipeline.Infrastructure.Sync;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Pipeline.Application;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Domain;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.Transmission.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Jobs;

/// <summary>
/// SYNC (PIP01d) — job planifié PAR TENANT (mécanique <c>ITenantJob</c>/<c>TenantJobRunner</c>, SOL06 ;
/// JAMAIS une boucle multi-tenant locale) de RÉCONCILIATION en lecture seule côté plateforme. Pour le tenant
/// courant : (1) résout le compte Plateforme Agréée actif et son plug-in via <see cref="IPaClientRegistry"/>
/// (jamais un plug-in PA concret — frontière P1, CLAUDE.md n°6/14) ; (2) pour CHAQUE document déjà <c>Issued</c>,
/// récupère auprès de la PA — SELON SES CAPACITÉS DÉCLARÉES (<see cref="PaCapabilities"/>), jamais un
/// <c>if (pa is …)</c> — la facture électronique générée (<see cref="PaCapabilities.SupportsDocumentRetrieval"/>)
/// et le(s) tax report(s) DGFiP (<see cref="PaCapabilities.SupportsTaxReportRetrieval"/>), et les ajoute en
/// ADDENDA chaînés au paquet WORM du document (TRK05). Une trace d'exécution (<c>pipeline.run_logs</c>) est
/// écrite à chaque exécution. Tenant-scopé : les services sont résolus depuis le scope tenant
/// (<see cref="TenantJobContext.Services"/>).
/// </summary>
/// <remarks>
/// <para>ATTRIBUTION DES TAX REPORTS — PAR DOCUMENT, jamais inventée (CLAUDE.md n°2/4) : un tax report DGFiP
/// (ledger de période) est rattaché à un document UNIQUEMENT s'il figure dans les <c>tax_report_ids</c> que la
/// PA renvoie POUR CE document (<see cref="IPaClient.GetDocumentStatusAsync"/>). Le contenu (XML) est lu depuis
/// la liste de compte (<see cref="IPaClient.ListTaxReportsAsync"/>, filtre <c>since</c> best-effort) indexée par
/// identifiant. Jamais de rattachement d'un ledger à un document qu'il ne couvre pas.</para>
/// <para>IDEMPOTENCE : <see cref="IArchiveService.AddAddendumAsync"/> adresse le fichier par EMPREINTE DE
/// CONTENU et scelle la ligne de chaîne de façon idempotente — re-jouer un SYNC ne duplique jamais un addendum
/// déjà présent (même facture / même XML = même empreinte = même entrée). Le coffre reste WORM, append-only.</para>
/// <para>Le SYNC ne fait avancer AUCUNE machine à états, n'écrit RIEN dans la base source et n'effectue AUCUNE
/// transmission : il enrichit la piste d'archive. Un tax report dont le XML n'est pas encore disponible
/// (génération batch DGFiP ~02:00, F05 §2) est simplement repris au cycle suivant — jamais un échec.</para>
/// </remarks>
public sealed partial class SyncTenantJob : ITenantJob
{
    /// <summary>Taille de page des lectures par état (file bornée — la console lit la même surface, TRK01).</summary>
    private const int PageSize = 100;

    private const string IssuedStateName = "Issued";
    private const string PaInvoiceAddendumKind = "facture-pa";
    private const string TaxReportAddendumKind = "tax-report";

    private readonly PipelineRunTrigger _trigger;

    /// <summary>Construit le job SYNC d'un tenant.</summary>
    /// <param name="trigger">Origine du déclenchement (planifié / manuel) — tracée dans le journal d'exécutions.</param>
    public SyncTenantJob(PipelineRunTrigger trigger = PipelineRunTrigger.Scheduled)
    {
        _trigger = trigger;
    }

    /// <inheritdoc />
    public string Name => "pipeline.sync";

    /// <inheritdoc />
    public async Task ExecuteAsync(TenantJobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var services = context.Services;
        var tenantId = context.TenantId;
        var timeProvider = services.GetRequiredService<TimeProvider>();
        var logger = services.GetRequiredService<ILogger<SyncTenantJob>>();
        var startedAt = timeProvider.GetUtcNow();

        var tenantSettings = services.GetRequiredService<ITenantSettingsQueries>();
        var companyId = await tenantSettings.GetCurrentCompanyId(cancellationToken);
        if (companyId is null)
        {
            // Profil tenant pas encore créé (CFG02) : rien à synchroniser (transitoire).
            await WriteRunLogAsync(
                services,
                timeProvider,
                _trigger,
                startedAt,
                new SyncTally(),
                "SYNC : aucun profil tenant (companyId) — rien à synchroniser.",
                cancellationToken);
            return;
        }

        var active = await ResolveActiveAccountAsync(tenantSettings, companyId.Value, cancellationToken);
        if (active is null)
        {
            LogNoActiveAccount(logger, tenantId);
            await WriteRunLogAsync(
                services,
                timeProvider,
                _trigger,
                startedAt,
                new SyncTally(),
                "SYNC : aucun compte Plateforme Agréée actif pour ce tenant — aucune synchronisation. Action opérateur : configurez et activez un compte PA (Paramétrage › Plateforme Agréée).",
                cancellationToken);
            return;
        }

        var registry = services.GetRequiredService<IPaClientRegistry>();
        var paClient = registry.Resolve(new PaAccountDescriptor(active.PluginType, tenantId));
        var capabilities = paClient.Capabilities;

        if (!capabilities.SupportsDocumentRetrieval && !capabilities.SupportsTaxReportRetrieval)
        {
            // La PA ne déclare AUCUNE capacité de récupération : rien à archiver en addendum (jamais un échec —
            // le comportement est piloté par les capacités, PAA01). Le produit n'est pas bloqué.
            LogNoSyncCapabilities(logger, tenantId, capabilities.PaName);
            await WriteRunLogAsync(
                services,
                timeProvider,
                _trigger,
                startedAt,
                new SyncTally(),
                string.Create(CultureInfo.InvariantCulture, $"SYNC : la Plateforme Agréée « {capabilities.PaName} » ne déclare ni récupération de facture générée ni récupération de tax report — aucun addendum."),
                cancellationToken);
            return;
        }

        // Liste des tax reports du compte (lecture seule ; `since` filtre best-effort, F05 §2), indexée par id.
        var reportsById = await LoadAvailableTaxReportsAsync(paClient, capabilities, cancellationToken);

        var tally = new SyncTally();
        await ForEachIssuedAsync(
            services,
            async id => tally.Add(await SafeProcessAsync(
                () => SyncDocumentAsync(services, paClient, capabilities, id, reportsById, logger, cancellationToken),
                id,
                logger,
                cancellationToken)),
            cancellationToken);

        await WriteRunLogAsync(services, timeProvider, _trigger, startedAt, tally, tally.Describe(), cancellationToken);
        LogSyncCompleted(logger, tenantId, tally.Invoices, tally.TaxReports, tally.Skipped, tally.Failed);
    }

    /// <summary>Premier compte Plateforme Agréée ACTIF du tenant, ou <c>null</c>.</summary>
    private static async Task<PaAccountDto?> ResolveActiveAccountAsync(
        ITenantSettingsQueries tenantSettings,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var accounts = await tenantSettings.GetPaAccounts(companyId, cancellationToken);
        foreach (var account in accounts)
        {
            if (account.IsActive)
            {
                return account;
            }
        }

        return null;
    }

    /// <summary>Liste les tax reports du compte (si la capacité est déclarée), indexés par identifiant.</summary>
    private static async Task<IReadOnlyDictionary<string, PaTaxReport>> LoadAvailableTaxReportsAsync(
        IPaClient paClient,
        PaCapabilities capabilities,
        CancellationToken cancellationToken)
    {
        var byId = new Dictionary<string, PaTaxReport>(StringComparer.Ordinal);
        if (!capabilities.SupportsTaxReportRetrieval)
        {
            return byId;
        }

        var reports = await paClient.ListTaxReportsAsync(since: null, cancellationToken);
        foreach (var report in reports)
        {
            if (!string.IsNullOrWhiteSpace(report.Id))
            {
                byId[report.Id] = report;
            }
        }

        return byId;
    }

    /// <summary>Synchronise UN document émis : facture PA + tax report(s) → addenda WORM, selon les capacités.</summary>
    private static async Task<SyncDocumentResult> SyncDocumentAsync(
        IServiceProvider services,
        IPaClient paClient,
        PaCapabilities capabilities,
        Guid documentId,
        IReadOnlyDictionary<string, PaTaxReport> reportsById,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var queries = services.GetRequiredService<IDocumentQueries>();
        var document = await queries.GetByIdAsync(documentId, cancellationToken);
        if (document is null
            || !string.Equals(document.State, IssuedStateName, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(document.PaDocumentId))
        {
            // Course de rejeu / document non émis / référence PA absente : rien à réconcilier (jamais un échec).
            return SyncDocumentResult.SkippedResult;
        }

        var archive = services.GetRequiredService<IArchiveService>();
        var invoices = 0;
        var taxReports = 0;

        // 1) Facture électronique générée par la PA (Factur-X / UBL / CII) → addendum WORM (idempotent).
        if (capabilities.SupportsDocumentRetrieval)
        {
            var generated = await paClient.GetGeneratedDocumentAsync(document.PaDocumentId!, cancellationToken);
            if (generated.Content is { Length: > 0 })
            {
                await archive.AddAddendumAsync(
                    BuildAddendum(
                        document,
                        PaInvoiceAddendumKind,
                        new ArchiveAttachment(
                            GeneratedInvoiceFileName(generated.Format),
                            GeneratedInvoiceContentType(generated.Format),
                            generated.Content)),
                    cancellationToken);
                invoices++;
            }
        }

        // 2) Tax report(s) DGFiP de CE document — attribution par les tax_report_ids renvoyés par la PA pour ce
        //    document (jamais un report d'un autre document, CLAUDE.md n°2/4) ; contenu lu depuis la liste de compte.
        if (capabilities.SupportsTaxReportRetrieval && reportsById.Count > 0)
        {
            var status = await paClient.GetDocumentStatusAsync(document.PaDocumentId!, cancellationToken);
            foreach (var reportId in status.TaxReportIds)
            {
                if (!reportsById.TryGetValue(reportId, out var report) || string.IsNullOrEmpty(report.XmlBase64))
                {
                    // XML pas encore généré (batch DGFiP ~02:00, F05 §2) : repris au cycle suivant, jamais un échec.
                    continue;
                }

                byte[] xml;
                try
                {
                    xml = Convert.FromBase64String(report.XmlBase64!);
                }
                catch (FormatException ex)
                {
                    // XML base64 illisible côté PA : on consigne et on poursuit (jamais un addendum corrompu).
                    LogTaxReportXmlInvalid(logger, document.Id, reportId, ex);
                    continue;
                }

                await archive.AddAddendumAsync(
                    BuildAddendum(
                        document,
                        TaxReportAddendumKind,
                        new ArchiveAttachment(TaxReportFileName(reportId), "application/xml", xml)),
                    cancellationToken);
                taxReports++;
            }
        }

        return new SyncDocumentResult(invoices, taxReports, Skipped: false, Failed: false);
    }

    private static ArchiveAddendumRequest BuildAddendum(DocumentDto document, string kind, ArchiveAttachment attachment) =>
        new()
        {
            DocumentId = document.Id,
            DocumentNumber = document.DocumentNumber,
            IssueDate = document.IssueDate,
            Kind = kind,
            Attachment = attachment,
        };

    private static string GeneratedInvoiceFileName(string? format) =>
        IsPdfInvoice(format) ? "facture-pa.pdf" : "facture-pa.xml";

    private static string GeneratedInvoiceContentType(string? format) =>
        IsPdfInvoice(format) ? "application/pdf" : "application/xml";

    /// <summary>Factur-X est un PDF/A-3 ; UBL / CII sont du XML.</summary>
    private static bool IsPdfInvoice(string? format) =>
        format is not null && format.Contains("Factur", StringComparison.OrdinalIgnoreCase);

    private static string TaxReportFileName(string reportId) =>
        string.Create(CultureInfo.InvariantCulture, $"tax-report-{reportId}.xml");

    /// <summary>
    /// Capture l'instantané des identifiants <c>Issued</c> AVANT traitement (pagination OFFSET fidèle).
    /// <para>LIMITE V1 (dette assumée, dédette ultérieure) : <c>Issued</c> est TERMINAL (le SYNC ne fait avancer
    /// aucune machine à états), donc l'ensemble s'accumule et chaque cycle re-balaye TOUT l'historique émis. Les
    /// ADDENDA sont idempotents (adressage par empreinte de contenu), mais les LECTURES PA par document
    /// (<c>GetGeneratedDocumentAsync</c>/<c>GetDocumentStatusAsync</c>) ne le sont pas : leur volume croît
    /// linéairement avec le cumul et peut heurter les rate-limits d'une PA en production. Bornage différé (curseur
    /// de dernière synchro / fenêtre de récence, ou marqueur « réconcilié » quand facture + tax reports attendus
    /// sont archivés) — hors périmètre PIP01d, car un document peut recevoir son tax report DGFiP des jours après
    /// l'émission (batch ~02:00), donc « réconcilié » n'est pas dérivable d'un simple flag à l'émission.</para>
    /// </summary>
    private static async Task<List<Guid>> SnapshotIssuedIdsAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var queries = services.GetRequiredService<IDocumentQueries>();
        var ids = new List<Guid>();
        var page = 1;
        while (true)
        {
            var batch = await queries.GetByStateAsync(IssuedStateName, page, PageSize, cancellationToken);
            foreach (var summary in batch)
            {
                ids.Add(summary.Id);
            }

            if (batch.Count < PageSize)
            {
                break;
            }

            page++;
        }

        return ids;
    }

    private static async Task ForEachIssuedAsync(IServiceProvider services, Func<Guid, Task> action, CancellationToken cancellationToken)
    {
        var ids = await SnapshotIssuedIdsAsync(services, cancellationToken);
        foreach (var id in ids)
        {
            await action(id);
        }
    }

    private static async Task<SyncDocumentResult> SafeProcessAsync(
        Func<Task<SyncDocumentResult>> process,
        Guid documentId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            return await process();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogDocumentSyncFailed(logger, documentId, ex);
            return SyncDocumentResult.FailedResult;
        }
    }

    private static async Task WriteRunLogAsync(
        IServiceProvider services,
        TimeProvider timeProvider,
        PipelineRunTrigger trigger,
        DateTimeOffset startedAt,
        SyncTally tally,
        string detail,
        CancellationToken cancellationToken)
    {
        var runLog = RunLog.Start(PipelineRunType.Sync, trigger, startedAt);
        runLog.Complete(
            completedAt: timeProvider.GetUtcNow(),
            documentsProcessed: tally.Processed,
            documentsSucceeded: tally.Succeeded,
            documentsFailed: tally.Failed,
            detail: detail);
        await services.GetRequiredService<IPipelineRunLogStore>().SaveAsync(runLog, cancellationToken);
    }

    [LoggerMessage(EventId = 7300, Level = LogLevel.Warning,
        Message = "SYNC : aucun compte Plateforme Agréée actif pour le tenant « {TenantId} » — aucune synchronisation.")]
    private static partial void LogNoActiveAccount(ILogger logger, string tenantId);

    [LoggerMessage(EventId = 7301, Level = LogLevel.Information,
        Message = "SYNC : la Plateforme Agréée « {PaName} » du tenant « {TenantId} » ne déclare aucune capacité de récupération — aucun addendum.")]
    private static partial void LogNoSyncCapabilities(ILogger logger, string tenantId, string paName);

    [LoggerMessage(EventId = 7302, Level = LogLevel.Information,
        Message = "SYNC terminé pour le tenant « {TenantId} » : {Invoices} facture(s) PA, {TaxReports} tax report(s) archivé(s), {Skipped} ignoré(s), {Failed} en échec.")]
    private static partial void LogSyncCompleted(ILogger logger, string tenantId, int invoices, int taxReports, int skipped, int failed);

    [LoggerMessage(EventId = 7303, Level = LogLevel.Warning,
        Message = "SYNC : XML base64 illisible pour le tax report « {ReportId} » du document {DocumentId} — addendum ignoré (jamais de pièce corrompue archivée).")]
    private static partial void LogTaxReportXmlInvalid(ILogger logger, Guid documentId, string reportId, Exception exception);

    [LoggerMessage(EventId = 7304, Level = LogLevel.Error,
        Message = "SYNC : échec inattendu sur le document {DocumentId} — document ignoré ce cycle, traitement du tenant poursuivi.")]
    private static partial void LogDocumentSyncFailed(ILogger logger, Guid documentId, Exception exception);
}
