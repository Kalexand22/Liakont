namespace Liakont.Modules.Pipeline.Infrastructure.Send;

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Lifecycle;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Pipeline.Application;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Domain;
using Liakont.Modules.Pipeline.Infrastructure.Serialization;
using Liakont.Modules.Staging.Contracts;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.Transmission.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Jobs;

/// <summary>
/// SEND (PIP01c) — job planifié PAR TENANT (mécanique <c>ITenantJob</c>/<c>TenantJobRunner</c>, SOL06 ;
/// JAMAIS une boucle multi-tenant locale). Pour le tenant courant : (1) résout le compte Plateforme Agréée
/// actif et son plug-in via <see cref="IPaClientRegistry"/> (jamais un plug-in PA concret — frontière P1,
/// CLAUDE.md n°6/14) ; (2) DIAGNOSTIC PA (F04 §3.1, piège « Transport not available ») : si le SIREN n'est
/// pas publié / le <c>tax_report_setting</c> n'est pas actif, alerte Warning et AUCUN envoi (documents
/// maintenus ReadyToSend) ; (3) PRE-SEND anti-doublon : raccroche les <c>Sending</c> restés en suspens
/// (crash) et vérifie côté PA avant tout renvoi / retry d'un <c>TechnicalError</c> ; (4) envoie les
/// <c>ReadyToSend</c> : sur émission, archive WORM (TRK05) PUIS purge du staging SUBORDONNÉE à la présence
/// EFFECTIVE du paquet (ADR-0014 §4) ; rejet PA et erreur technique CONSERVENT le staging. Une trace
/// d'exécution (<c>pipeline.run_logs</c>) est écrite à chaque exécution. Tenant-scopé : les services sont
/// résolus depuis le scope tenant (<see cref="TenantJobContext.Services"/>).
/// </summary>
/// <remarks>
/// <para>ANTI-DOUBLON (F05/F06, TRK03) à DEUX niveaux complémentaires : (a) avant tout renvoi, si le
/// document porte déjà une référence PA, on interroge <see cref="IPaClient.GetDocumentStatusAsync"/> — si la
/// PA connaît le document, on le finalise <c>Issued</c> SANS renvoyer ; (b) filet robuste pour les renvois
/// après crash/timeout sans référence connue : la PA déduplique par numéro de document (F05), donc un renvoi
/// d'un document déjà émis retourne le résultat d'origine SANS nouvelle émission. La référence PA portée par
/// le document (<see cref="DocumentDto.PaDocumentId"/>) est réservée « en aval » (migration Documents) :
/// le contrôle (a) est donc opportuniste et compatible en avant ; la garantie de fond est (b).</para>
/// <para>DRY-RUN (« tout sauf écritures PA ») : dénombre les <c>ReadyToSend</c>, n'appelle AUCUNE écriture PA
/// et ne fait avancer aucun document (le diagnostic en lecture seule reste effectué).</para>
/// </remarks>
public sealed partial class SendTenantJob : ITenantJob
{
    /// <summary>Taille de page des lectures par état (file bornée — la console lit la même surface, TRK01).</summary>
    private const int PageSize = 100;

    private const string ReadyToSendStateName = "ReadyToSend";
    private const string SendingStateName = "Sending";
    private const string TechnicalErrorStateName = "TechnicalError";

    /// <summary>Motif de blocage quand le contenu stagé d'un document à envoyer est altéré/illisible (intégrité).</summary>
    private const string StagingIntegrityReason =
        "Le contenu stagé du document est altéré ou illisible (contrôle d'intégrité) : envoi impossible sans " +
        "risquer de transmettre une donnée fausse. Document bloqué. Action opérateur : relancez l'extraction " +
        "du document depuis le logiciel source (l'agent le re-poussera) ; si le problème persiste, contactez le support.";

    private readonly PipelineRunTrigger _trigger;
    private readonly bool _dryRun;

    /// <summary>Construit le job SEND d'un tenant (déclencheur + mode simulation).</summary>
    /// <param name="trigger">Origine du déclenchement (planifié / manuel) — tracée dans le journal d'exécutions.</param>
    /// <param name="dryRun">Si <c>true</c>, simule sans aucune écriture PA ni transition de document.</param>
    public SendTenantJob(PipelineRunTrigger trigger = PipelineRunTrigger.Scheduled, bool dryRun = false)
    {
        _trigger = trigger;
        _dryRun = dryRun;
    }

    /// <inheritdoc />
    public string Name => "pipeline.send";

    /// <inheritdoc />
    public async Task ExecuteAsync(TenantJobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var services = context.Services;
        var tenantId = context.TenantId;
        var timeProvider = services.GetRequiredService<TimeProvider>();
        var logger = services.GetRequiredService<ILogger<SendTenantJob>>();
        var startedAt = timeProvider.GetUtcNow();

        var tenantSettings = services.GetRequiredService<ITenantSettingsQueries>();
        var companyId = await tenantSettings.GetCurrentCompanyId(cancellationToken);
        if (companyId is null)
        {
            // Profil tenant pas encore créé (CFG02) : rien à envoyer (transitoire). Pas d'envoi à l'aveugle.
            await WriteRunLogAsync(
                services,
                timeProvider,
                _trigger,
                startedAt,
                new SendTally(),
                "SEND : aucun profil tenant (companyId) — rien à envoyer.",
                cancellationToken);
            return;
        }

        // Compte PA actif + plug-in du tenant (résolution par capacités/clé, jamais un if (pa is …)).
        var active = await ResolveActiveAccountAsync(tenantSettings, companyId.Value, cancellationToken);
        if (active is null)
        {
            LogNoActiveAccount(logger, tenantId);
            await WriteRunLogAsync(
                services,
                timeProvider,
                _trigger,
                startedAt,
                new SendTally(),
                "SEND : aucun compte Plateforme Agréée actif pour ce tenant — aucun envoi. Action opérateur : configurez et activez un compte PA (Paramétrage › Plateforme Agréée).",
                cancellationToken);
            return;
        }

        var registry = services.GetRequiredService<IPaClientRegistry>();
        var paClient = registry.Resolve(new PaAccountDescriptor(active.PluginType, tenantId));

        // DIAGNOSTIC PA (F04 §3.1) : SIREN publié / tax_report_setting actif. Une LECTURE — autorisée même en dry-run.
        var setting = await paClient.GetTaxReportSettingAsync(cancellationToken);
        if (!IsTaxReportSettingActive(setting, timeProvider))
        {
            LogTransportNotAvailable(logger, tenantId);
            await WriteRunLogAsync(
                services,
                timeProvider,
                _trigger,
                startedAt,
                new SendTally(),
                "SEND : SIREN non publié / paramétrage de transmission (tax_report_setting) inactif côté Plateforme Agréée — aucun envoi (documents maintenus ReadyToSend). Action opérateur : faites publier le SIREN auprès de la PA, puis relancez l'envoi.",
                cancellationToken);
            return;
        }

        if (_dryRun)
        {
            var ready = await CountByStateAsync(services, ReadyToSendStateName, cancellationToken);
            var dryTally = new SendTally { Processed = ready };
            await WriteRunLogAsync(
                services,
                timeProvider,
                _trigger,
                startedAt,
                dryTally,
                string.Create(CultureInfo.InvariantCulture, $"SEND (dry-run) : {ready} document(s) prêt(s) à l'envoi — aucune écriture PA, aucune transition."),
                cancellationToken);
            return;
        }

        var tally = new SendTally();

        // 1) PRE-SEND : raccroche les Sending restés en suspens (crash) — repris dès le 1er cycle (TRK03).
        var sending = await services.GetRequiredService<IDocumentQueries>().GetPotentiallySentDocumentsAsync(cancellationToken);
        foreach (var summary in sending)
        {
            tally.Add(await RecoverSendingAsync(services, paClient, tenantId, summary.Id, logger, cancellationToken));
        }

        // 2) Retry des TechnicalError (anti-doublon vérifié AVANT tout renvoi).
        await ForEachByStateAsync(
            services,
            TechnicalErrorStateName,
            async id => tally.Add(await RetryTechnicalErrorAsync(services, paClient, tenantId, id, logger, cancellationToken)),
            cancellationToken);

        // 3) Envoi des ReadyToSend.
        await ForEachByStateAsync(
            services,
            ReadyToSendStateName,
            async id => tally.Add(await SendReadyAsync(services, paClient, tenantId, id, logger, cancellationToken)),
            cancellationToken);

        await WriteRunLogAsync(services, timeProvider, _trigger, startedAt, tally, tally.Describe(), cancellationToken);
        LogSendCompleted(logger, tenantId, tally.Succeeded, tally.Failed, tally.Deferred, tally.Skipped);
    }

    /// <summary>Premier compte Plateforme Agréée ACTIF du tenant (l'envoi passe par lui), ou <c>null</c>.</summary>
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

    /// <summary>Raccroche un document resté <c>Sending</c> (crash) : vérifie la PA si une référence est connue, sinon renvoie.</summary>
    private static async Task<SendOutcome> RecoverSendingAsync(
        IServiceProvider services,
        IPaClient paClient,
        string tenantId,
        Guid documentId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var queries = services.GetRequiredService<IDocumentQueries>();
        var document = await queries.GetByIdAsync(documentId, cancellationToken);
        if (document is null || !string.Equals(document.State, SendingStateName, StringComparison.Ordinal))
        {
            return SendOutcome.Skipped;
        }

        var staged = await ReadStagedPivotAsync(services, tenantId, document, logger, cancellationToken);
        if (staged.Status == StagedReadStatus.NotStaged)
        {
            return SendOutcome.Deferred;
        }

        if (staged.Status == StagedReadStatus.Integrity)
        {
            await services.GetRequiredService<IDocumentLifecycle>().BlockAsync(
                documentId, WithDocumentNumber(document.DocumentNumber, StagingIntegrityReason), cancellationToken);
            return SendOutcome.Failed;
        }

        if (await TryFinalizeFromPaStatusAsync(services, paClient, tenantId, document, staged.Pivot!, staged.Json!, beginSending: false, logger, cancellationToken))
        {
            return SendOutcome.Succeeded;
        }

        // Pas de référence connue / la PA ne connaît pas le document : on renvoie (déjà Sending). La PA
        // déduplique par numéro (F05) : un document déjà émis revient Issued SANS nouvelle émission.
        var result = await paClient.SendDocumentAsync(staged.Pivot!, sendAfterImport: true, cancellationToken);
        return await HandleSendResultAsync(services, tenantId, document, staged.Pivot!, staged.Json!, result, logger, cancellationToken);
    }

    /// <summary>Retente un document <c>TechnicalError</c> : anti-doublon d'abord, puis TechnicalError → ReadyToSend → Sending → envoi.</summary>
    private static async Task<SendOutcome> RetryTechnicalErrorAsync(
        IServiceProvider services,
        IPaClient paClient,
        string tenantId,
        Guid documentId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var queries = services.GetRequiredService<IDocumentQueries>();
        var document = await queries.GetByIdAsync(documentId, cancellationToken);
        if (document is null || !string.Equals(document.State, TechnicalErrorStateName, StringComparison.Ordinal))
        {
            return SendOutcome.Skipped;
        }

        if (string.IsNullOrWhiteSpace(document.MappingVersion))
        {
            // Un document parvenu en envoi a toujours une version de mapping (posée au CHECK) : son absence
            // est une anomalie de données — on ne renvoie pas à l'aveugle.
            LogMissingMappingVersion(logger, documentId);
            return SendOutcome.Skipped;
        }

        var staged = await ReadStagedPivotAsync(services, tenantId, document, logger, cancellationToken);
        if (staged.Status == StagedReadStatus.NotStaged)
        {
            return SendOutcome.Deferred;
        }

        if (staged.Status == StagedReadStatus.Integrity)
        {
            await services.GetRequiredService<IDocumentLifecycle>().BlockAsync(
                documentId, WithDocumentNumber(document.DocumentNumber, StagingIntegrityReason), cancellationToken);
            return SendOutcome.Failed;
        }

        var lifecycle = services.GetRequiredService<IDocumentLifecycle>();

        // Reprise : TechnicalError → ReadyToSend (version de mapping déjà posée au CHECK, on la reconsigne).
        await lifecycle.MarkReadyToSendAsync(documentId, document.MappingVersion!, cancellationToken);

        // Anti-doublon AVANT tout renvoi : si la PA connaît déjà le document, on le finalise sans réémettre.
        if (await TryFinalizeFromPaStatusAsync(services, paClient, tenantId, document, staged.Pivot!, staged.Json!, beginSending: true, logger, cancellationToken))
        {
            return SendOutcome.Succeeded;
        }

        await lifecycle.BeginSendingAsync(documentId, cancellationToken);
        var result = await paClient.SendDocumentAsync(staged.Pivot!, sendAfterImport: true, cancellationToken);
        return await HandleSendResultAsync(services, tenantId, document, staged.Pivot!, staged.Json!, result, logger, cancellationToken);
    }

    /// <summary>Envoie un document <c>ReadyToSend</c> : ReadyToSend → Sending → envoi → issue.</summary>
    private static async Task<SendOutcome> SendReadyAsync(
        IServiceProvider services,
        IPaClient paClient,
        string tenantId,
        Guid documentId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var queries = services.GetRequiredService<IDocumentQueries>();
        var document = await queries.GetByIdAsync(documentId, cancellationToken);
        if (document is null || !string.Equals(document.State, ReadyToSendStateName, StringComparison.Ordinal))
        {
            return SendOutcome.Skipped;
        }

        var staged = await ReadStagedPivotAsync(services, tenantId, document, logger, cancellationToken);
        if (staged.Status == StagedReadStatus.NotStaged)
        {
            return SendOutcome.Deferred;
        }

        if (staged.Status == StagedReadStatus.Integrity)
        {
            await services.GetRequiredService<IDocumentLifecycle>().BlockAsync(
                documentId, WithDocumentNumber(document.DocumentNumber, StagingIntegrityReason), cancellationToken);
            return SendOutcome.Failed;
        }

        // Garde-fou avoirs : un avoir vers une PA sans capacité avoirs reste ReadyToSend (traité par PIP02),
        // jamais bloqué ni envoyé à l'aveugle (l'état machine interdit un retour Sending → ReadyToSend).
        if (staged.Pivot!.CreditNoteRefs.Count > 0 && !paClient.Capabilities.SupportsCreditNotes)
        {
            LogCreditNoteCapabilityMissing(logger, documentId, paClient.Capabilities.PaName);
            return SendOutcome.Skipped;
        }

        await services.GetRequiredService<IDocumentLifecycle>().BeginSendingAsync(documentId, cancellationToken);
        var result = await paClient.SendDocumentAsync(staged.Pivot!, sendAfterImport: true, cancellationToken);
        return await HandleSendResultAsync(services, tenantId, document, staged.Pivot!, staged.Json!, result, logger, cancellationToken);
    }

    /// <summary>
    /// Anti-doublon par interrogation de la PA : si le document porte une référence PA connue et que la PA le
    /// déclare <c>Issued</c>, on le finalise (archive + purge) SANS renvoyer. Retourne <c>false</c> si aucune
    /// référence n'est connue ou si la PA ne connaît pas (encore) le document. <paramref name="beginSending"/>
    /// engage la transition Sending requise avant la finalisation (cas retry depuis ReadyToSend).
    /// </summary>
    private static async Task<bool> TryFinalizeFromPaStatusAsync(
        IServiceProvider services,
        IPaClient paClient,
        string tenantId,
        DocumentDto document,
        PivotDocumentDto pivot,
        string canonicalJson,
        bool beginSending,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(document.PaDocumentId))
        {
            return false;
        }

        var status = await paClient.GetDocumentStatusAsync(document.PaDocumentId!, cancellationToken);
        if (status.State != PaSendState.Issued)
        {
            return false;
        }

        if (beginSending)
        {
            await services.GetRequiredService<IDocumentLifecycle>().BeginSendingAsync(document.Id, cancellationToken);
        }

        var paResponseJson = SendPaSnapshot.FromStatus(status);
        await FinalizeIssuedAsync(services, tenantId, document, pivot, canonicalJson, paResponseJson, cancellationToken);
        LogAntiDuplicateFinalized(logger, document.Id);
        return true;
    }

    /// <summary>Aiguille l'issue d'un envoi (déjà <c>Sending</c>) vers la transition finale et les effets associés.</summary>
    private static async Task<SendOutcome> HandleSendResultAsync(
        IServiceProvider services,
        string tenantId,
        DocumentDto document,
        PivotDocumentDto pivot,
        string canonicalJson,
        PaSendResult result,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var lifecycle = services.GetRequiredService<IDocumentLifecycle>();
        switch (result.State)
        {
            case PaSendState.Issued:
                await FinalizeIssuedAsync(services, tenantId, document, pivot, canonicalJson, SendPaSnapshot.FromSendResult(result), cancellationToken);
                return SendOutcome.Succeeded;

            case PaSendState.RejectedByPa:
                // Rejet métier : staging CONSERVÉ (contenu requis pour correction/resoumission — ADR-0014, jamais en WORM).
                await lifecycle.MarkRejectedByPaAsync(
                    document.Id,
                    new DocumentRejectionSnapshots { PayloadSnapshot = canonicalJson, PaResponseSnapshot = SendPaSnapshot.FromSendResult(result) },
                    cancellationToken);
                LogRejected(logger, document.Id);
                return SendOutcome.Failed;

            case PaSendState.TechnicalError:
                // Erreur technique : staging CONSERVÉ, re-tentable au prochain cycle.
                await lifecycle.MarkTechnicalErrorAsync(document.Id, cancellationToken);
                LogTechnicalError(logger, document.Id);
                return SendOutcome.Failed;

            default:
                // New / CapabilityNotSupported inattendus pour un envoi de document (les avoirs sans capacité
                // sont écartés AVANT l'envoi) : on retombe sur une erreur technique re-tentable plutôt que
                // d'inventer une issue ou de laisser le document figé en Sending.
                await lifecycle.MarkTechnicalErrorAsync(document.Id, cancellationToken);
                LogUnexpectedSendState(logger, document.Id, result.State.ToString());
                return SendOutcome.Failed;
        }
    }

    /// <summary>Émission : MarkIssued (preuve) PUIS archive WORM (TRK05) PUIS purge du staging subordonnée au WORM (ADR-0014 §4).</summary>
    private static async Task FinalizeIssuedAsync(
        IServiceProvider services,
        string tenantId,
        DocumentDto document,
        PivotDocumentDto pivot,
        string canonicalJson,
        string paResponseJson,
        CancellationToken cancellationToken)
    {
        var mappingTraceJson = string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"mappingVersion\":\"{document.MappingVersion ?? "(non précisée)"}\"}}");

        await services.GetRequiredService<IDocumentLifecycle>().MarkIssuedAsync(
            document.Id,
            new DocumentIssuanceSnapshots { PayloadSnapshot = canonicalJson, PaResponseSnapshot = paResponseJson, MappingTrace = mappingTraceJson },
            cancellationToken);

        var archiveRequest = SendArchiveComposer.Compose(document, pivot, canonicalJson, paResponseJson, mappingTraceJson);
        await services.GetRequiredService<IArchiveService>().ArchiveIssuedDocumentAsync(archiveRequest, cancellationToken);

        // Purge subordonnée à la présence EFFECTIVE du paquet WORM (jamais à la seule étiquette Issued).
        var key = new StagedPayloadKey(tenantId, document.Id, document.PayloadHash);
        var locator = new ArchivedDocumentLocator(document.Id, document.IssueDate.Year, document.IssueDate.Month, document.DocumentNumber);
        await services.GetRequiredService<IStagingPurgeService>().PurgeIfArchivedAsync(key, locator, cancellationToken);
    }

    /// <summary>Relit le pivot stagé (PIP00) et re-vérifie l'intégrité. Absence = transitoire ; altération = à bloquer.</summary>
    private static async Task<StagedRead> ReadStagedPivotAsync(
        IServiceProvider services,
        string tenantId,
        DocumentDto document,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var staging = services.GetRequiredService<IPayloadStagingStore>();
        var key = new StagedPayloadKey(tenantId, document.Id, document.PayloadHash);
        try
        {
            var canonicalJson = await staging.ReadAsync(key, cancellationToken);
            var pivot = PivotCanonicalJsonReader.Read(canonicalJson);
            return new StagedRead(StagedReadStatus.Ok, pivot, canonicalJson);
        }
        catch (StagedPayloadNotFoundException)
        {
            // Transitoire (ADR-0014) : le contenu peut arriver/être re-poussé — on diffère, jamais terminal.
            LogStagingNotYetAvailable(logger, document.Id, tenantId);
            return new StagedRead(StagedReadStatus.NotStaged, null, null);
        }
        catch (StagedPayloadIntegrityException ex)
        {
            LogStagingIntegrityFailure(logger, document.Id, tenantId, ex);
            return new StagedRead(StagedReadStatus.Integrity, null, null);
        }
    }

    /// <summary>SIREN publié / paramétrage de transmission actif : <c>StartDate</c> renseignée et non future (F04 §3.1).</summary>
    private static bool IsTaxReportSettingActive(PaTaxReportSetting setting, TimeProvider timeProvider)
    {
        if (setting.StartDate is not { } startDate)
        {
            return false;
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        return startDate <= today;
    }

    private static string WithDocumentNumber(string documentNumber, string reason) =>
        string.Create(CultureInfo.InvariantCulture, $"Document n° {documentNumber} : {reason}");

    private static async Task<int> CountByStateAsync(
        IServiceProvider services,
        string state,
        CancellationToken cancellationToken)
    {
        var count = 0;
        await ForEachByStateAsync(
            services,
            state,
            _ =>
            {
                count++;
                return Task.CompletedTask;
            },
            cancellationToken);
        return count;
    }

    /// <summary>Parcourt, page par page, les documents d'un état donné (file bornée TRK01) et applique une action par identifiant.</summary>
    private static async Task ForEachByStateAsync(
        IServiceProvider services,
        string state,
        Func<Guid, Task> action,
        CancellationToken cancellationToken)
    {
        var queries = services.GetRequiredService<IDocumentQueries>();
        var page = 1;
        while (true)
        {
            var batch = await queries.GetByStateAsync(state, page, PageSize, cancellationToken);
            foreach (var summary in batch)
            {
                await action(summary.Id);
            }

            if (batch.Count < PageSize)
            {
                break;
            }

            page++;
        }
    }

    private static async Task WriteRunLogAsync(
        IServiceProvider services,
        TimeProvider timeProvider,
        PipelineRunTrigger trigger,
        DateTimeOffset startedAt,
        SendTally tally,
        string detail,
        CancellationToken cancellationToken)
    {
        var runLog = RunLog.Start(PipelineRunType.Send, trigger, startedAt);
        runLog.Complete(
            completedAt: timeProvider.GetUtcNow(),
            documentsProcessed: tally.Processed,
            documentsSucceeded: tally.Succeeded,
            documentsFailed: tally.Failed,
            detail: detail);
        await services.GetRequiredService<IPipelineRunLogStore>().SaveAsync(runLog, cancellationToken);
    }

    [LoggerMessage(EventId = 7200, Level = LogLevel.Warning,
        Message = "SEND : aucun compte Plateforme Agréée actif pour le tenant « {TenantId} » — aucun envoi.")]
    private static partial void LogNoActiveAccount(ILogger logger, string tenantId);

    [LoggerMessage(EventId = 7201, Level = LogLevel.Warning,
        Message = "SEND : SIREN non publié / tax_report_setting inactif pour le tenant « {TenantId} » (« Transport not available », F04 §3.1) — aucun envoi.")]
    private static partial void LogTransportNotAvailable(ILogger logger, string tenantId);

    [LoggerMessage(EventId = 7202, Level = LogLevel.Information,
        Message = "SEND terminé pour le tenant « {TenantId} » : {Succeeded} émis, {Failed} en échec, {Deferred} différés, {Skipped} ignorés.")]
    private static partial void LogSendCompleted(ILogger logger, string tenantId, int succeeded, int failed, int deferred, int skipped);

    [LoggerMessage(EventId = 7203, Level = LogLevel.Information,
        Message = "SEND : contenu pas encore stagé pour le document {DocumentId} (tenant « {TenantId} ») — différé (transitoire, ADR-0014).")]
    private static partial void LogStagingNotYetAvailable(ILogger logger, Guid documentId, string tenantId);

    [LoggerMessage(EventId = 7204, Level = LogLevel.Error,
        Message = "SEND : contenu stagé altéré/illisible pour le document {DocumentId} (tenant « {TenantId} ») — document bloqué (intégrité).")]
    private static partial void LogStagingIntegrityFailure(ILogger logger, Guid documentId, string tenantId, Exception exception);

    [LoggerMessage(EventId = 7205, Level = LogLevel.Information,
        Message = "SEND : document {DocumentId} déjà connu de la Plateforme Agréée — finalisé Issued sans renvoi (anti-doublon).")]
    private static partial void LogAntiDuplicateFinalized(ILogger logger, Guid documentId);

    [LoggerMessage(EventId = 7206, Level = LogLevel.Warning,
        Message = "SEND : document {DocumentId} rejeté par la Plateforme Agréée (staging conservé pour correction).")]
    private static partial void LogRejected(ILogger logger, Guid documentId);

    [LoggerMessage(EventId = 7207, Level = LogLevel.Warning,
        Message = "SEND : erreur technique de transmission pour le document {DocumentId} (staging conservé, re-tentable).")]
    private static partial void LogTechnicalError(ILogger logger, Guid documentId);

    [LoggerMessage(EventId = 7208, Level = LogLevel.Warning,
        Message = "SEND : issue d'envoi inattendue « {State} » pour le document {DocumentId} — marqué erreur technique (re-tentable).")]
    private static partial void LogUnexpectedSendState(ILogger logger, Guid documentId, string state);

    [LoggerMessage(EventId = 7209, Level = LogLevel.Warning,
        Message = "SEND : avoir {DocumentId} non envoyé — la Plateforme Agréée « {PaName} » ne déclare pas la capacité avoirs (maintenu ReadyToSend, traité par le pipeline des avoirs).")]
    private static partial void LogCreditNoteCapabilityMissing(ILogger logger, Guid documentId, string paName);

    [LoggerMessage(EventId = 7210, Level = LogLevel.Warning,
        Message = "SEND : document {DocumentId} sans version de mapping en envoi — anomalie de données, non renvoyé.")]
    private static partial void LogMissingMappingVersion(ILogger logger, Guid documentId);
}
