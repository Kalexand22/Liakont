namespace Liakont.Modules.Pipeline.Infrastructure.Send;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Serialization;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Lifecycle;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Mandats.Contracts.DTOs;
using Liakont.Modules.Mandats.Contracts.Queries;
using Liakont.Modules.Pipeline.Application;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Domain;
using Liakont.Modules.Pipeline.Infrastructure.Check;
using Liakont.Modules.Pipeline.Infrastructure.Serialization;
using Liakont.Modules.Staging.Contracts;
using Liakont.Modules.SupportTrace.Contracts;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.Transmission.Contracts;
using Liakont.Modules.TvaMapping.Contracts.Services;
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
    private const string BlockedStateName = "Blocked";

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

        // DIAGNOSTIC PA (F04 §3.1) : SIREN publié / tax_report_setting actif. Une LECTURE — autorisée même en
        // dry-run. Piloté par CAPACITÉ (jamais if (pa is …), CLAUDE.md n°8) : ce pré-requis vaut pour les PA de
        // niveau « Pilotage » (e-reporting, où l'envoi exige un SIREN publié côté PA). Une PA de niveau
        // « Essentiel » qui ne fait que TRANSMETTRE un Factur-X pré-construit (SupportsFacturXTransmission,
        // email/dépôt de fichier — F16 §6) n'a AUCUN tax_report_setting : la sauter ici est la seule façon
        // qu'elle ait pour transmettre (sinon son réglage neutre faute « Transport not available »).
        if (!paClient.Capabilities.SupportsFacturXTransmission)
        {
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

        // Émetteur (profil tenant + paramétrage fiscal) résolu UNE fois par exécution de job et propagé à
        // l'enrichissement read-time de CHAQUE document (RB9) — aucune relecture tenant par document (évite un
        // N+1 sur le lot ; symétrique au hoist de companyId). Snapshot cohérent pour toute la passe SEND.
        var tenantProfile = await tenantSettings.GetTenantProfile(companyId.Value, cancellationToken);
        var fiscalSettings = await tenantSettings.GetFiscalSettings(companyId.Value, cancellationToken);

        // 1) PRE-SEND : raccroche les Sending restés en suspens (crash) — repris dès le 1er cycle (TRK03).
        var sending = await services.GetRequiredService<IDocumentQueries>().GetPotentiallySentDocumentsAsync(cancellationToken);
        foreach (var summary in sending)
        {
            tally.Add(await SafeProcessAsync(() => RecoverSendingAsync(services, paClient, active, timeProvider, tenantId, companyId.Value, tenantProfile, fiscalSettings, summary.Id, logger, cancellationToken), summary.Id, logger, cancellationToken));
        }

        // 2) Retry des TechnicalError (anti-doublon vérifié AVANT tout renvoi).
        await ForEachByStateAsync(
            services,
            TechnicalErrorStateName,
            async id => tally.Add(await SafeProcessAsync(() => RetryTechnicalErrorAsync(services, paClient, active, timeProvider, tenantId, companyId.Value, tenantProfile, fiscalSettings, id, logger, cancellationToken), id, logger, cancellationToken)),
            cancellationToken);

        // 3) Envoi des ReadyToSend (les FACTURES d'origine des avoirs sont émises ICI, avant la réconciliation).
        await SendReadyToSendPassAsync(services, paClient, active, timeProvider, tenantId, companyId.Value, tenantProfile, fiscalSettings, tally, logger, cancellationToken);

        // 4) RÉORDONNANCEMENT DES AVOIRS (PIP02, F07 §B.5) : un avoir resté Blocked parce que sa facture d'origine
        //    n'était pas (encore) émise est ré-évalué dès lors que l'origine est désormais émise (Blocked →
        //    ReadyToSend). Cela GARANTIT l'ordre chronologique « l'avoir après sa facture d'origine » : l'origine
        //    vient d'être émise en (3), l'avoir débloqué est envoyé en (5) — jamais l'inverse.
        var unblockedCreditNotes = await ReconcileCreditNotesAsync(services, companyId.Value, tenantProfile, fiscalSettings, tenantId, logger, cancellationToken);

        // 5) Envoi CIBLÉ des avoirs fraîchement débloqués (on n'envoie QUE ces IDs, jamais un re-snapshot de tous
        //    les ReadyToSend : sinon les différés / ignorés de l'étape 3 seraient recomptés dans la trace SEND).
        foreach (var documentId in unblockedCreditNotes)
        {
            tally.Add(await SafeProcessAsync(() => SendReadyAsync(services, paClient, active, timeProvider, tenantId, companyId.Value, tenantProfile, fiscalSettings, documentId, logger, cancellationToken), documentId, logger, cancellationToken));
        }

        var detail = unblockedCreditNotes.Count > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{tally.Describe()} {unblockedCreditNotes.Count} avoir(s) débloqué(s) (facture d'origine émise — réordonnancement F07 §B.5).")
            : tally.Describe();
        await WriteRunLogAsync(services, timeProvider, _trigger, startedAt, tally, detail, cancellationToken);
        LogSendCompleted(logger, tenantId, tally.Succeeded, tally.Failed, tally.Deferred, tally.Skipped);
    }

    /// <summary>Une passe d'envoi des <c>ReadyToSend</c> du tenant (snapshot d'IDs → envoi un par un, isolé).</summary>
    private static Task SendReadyToSendPassAsync(
        IServiceProvider services,
        IPaClient paClient,
        PaAccountDto account,
        TimeProvider timeProvider,
        string tenantId,
        Guid companyId,
        TenantProfileDto? tenantProfile,
        FiscalSettingsDto? fiscalSettings,
        SendTally tally,
        ILogger logger,
        CancellationToken cancellationToken) =>
        ForEachByStateAsync(
            services,
            ReadyToSendStateName,
            async id => tally.Add(await SafeProcessAsync(() => SendReadyAsync(services, paClient, account, timeProvider, tenantId, companyId, tenantProfile, fiscalSettings, id, logger, cancellationToken), id, logger, cancellationToken)),
            cancellationToken);

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
        PaAccountDto account,
        TimeProvider timeProvider,
        string tenantId,
        Guid companyId,
        TenantProfileDto? tenantProfile,
        FiscalSettingsDto? fiscalSettings,
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

        var staged = await ReadStagedPivotAsync(services, tenantId, companyId, tenantProfile, fiscalSettings, document, logger, cancellationToken);
        if (staged.Status == StagedReadStatus.NotStaged)
        {
            return SendOutcome.Deferred;
        }

        if (staged.Status == StagedReadStatus.Integrity)
        {
            // Le document n'est plus Detected : la machine à états n'offre AUCUNE cible légale « intégrité »
            // depuis ReadyToSend/Sending/TechnicalError (F06 §3 / TRK02). On NE transitionne PAS (on n'invente
            // pas d'état) ; on consigne une erreur opérateur et on N'ENVOIE JAMAIS un contenu altéré
            // (« bloquer plutôt qu'envoyer faux »). Le document est ré-examiné chaque cycle jusqu'à ré-extraction.
            LogStagingIntegrityNotSent(logger, documentId, tenantId);
            return SendOutcome.Failed;
        }

        if (staged.Status is StagedReadStatus.EmitterUnresolved or StagedReadStatus.TvaUnresolved)
        {
            // Émetteur non résolu (profil vidé entre CHECK et SEND, RB9) OU catégorie TVA non reposée (table de
            // mapping modifiée depuis le CHECK) : HOLD — aucune transmission ni archive. Différé : repris dès que
            // le profil / la table sont rétablis (la cause précise est déjà journalisée dans ReadStagedPivotAsync).
            return SendOutcome.Deferred;
        }

        // Anti double-dépôt ASYNCHRONE (item PIPE01, D7) : si le document porte DÉJÀ une référence PA (flux
        // déposé à un cycle précédent sur une PA asynchrone, persistée par RecordPaSendingReferenceAsync), le
        // raccrochage est AUTORITAIRE — on RELIT le statut par cette référence et on finalise sur un état
        // TERMINAL (Issued / RejectedByPa), sinon on MAINTIENT Sending. On ne RE-DÉPOSE JAMAIS un flux déjà
        // accepté : une PA asynchrone (Chorus Pro) crée un nouveau flux à chaque dépôt → un renvoi = double
        // dépôt = double déclaration fiscale (CLAUDE.md n°3). Court-circuite avant tout chemin de (re)transmission.
        if (!string.IsNullOrWhiteSpace(document.PaDocumentId))
        {
            return await FinalizeFromAsyncPaReferenceAsync(services, paClient, tenantId, document, staged.Pivot!, staged.Json!, logger, cancellationToken);
        }

        if (IsUnsendableCreditNote(staged.Pivot!, paClient))
        {
            // Avoir vers une PA sans capacité avoirs : laissé en l'état (aucune transition), traité par PIP02 —
            // jamais renvoyé ni reclassé (pas de double comptage entre phases).
            LogCreditNoteCapabilityMissing(logger, document.Id, paClient.Capabilities.PaName);
            return SendOutcome.Skipped;
        }

        // Garde autofacturation 389 (MND07) : un self-billed n'est projeté/émis que vers une PA capable et
        // avec son BT-1 fiscal alloué (MND05) ; sinon maintenu en l'état (jamais émis faux).
        var selfBilled = await ResolveSelfBilledSendAsync(services, paClient, companyId, document.Id, staged.Pivot!, logger, cancellationToken);
        if (selfBilled.Hold)
        {
            return SendOutcome.Skipped;
        }

        // Garde anti double-envoi pour une PA SANS dédoublonnage propre (Essentiel : la générique email/dépôt
        // ne déduplique pas, GetDocumentStatus = CapabilityNotSupported). Un cycle précédent a pu transmettre +
        // journaliser ce Factur-X puis crasher AVANT MarkIssued : la journalisation FX06/FX07 (clé d'idempotence
        // = numéro de document) est ALORS la preuve de transmission. Auto-gating : les PA Pilotage ne
        // journalisent jamais ⇒ no-op (leur filet reste le dédoublonnage PA par numéro, ci-dessous).
        if (await TryFinalizeFromJournalAsync(services, tenantId, document, staged.Pivot!, staged.Json!, beginSending: false, logger, cancellationToken))
        {
            return SendOutcome.Succeeded;
        }

        // Aucune transmission journalisée : on (re)transmet (déjà Sending). Pilotage : la PA déduplique par numéro (F05). Essentiel : le destinataire dédoublonne par numéro (BT-1) — at-least-once assumé (canal externe).
        var (result, facturX) = await TransmitAsync(services, paClient, account, timeProvider, staged.Pivot!, selfBilled.Projection, cancellationToken);
        return await HandleSendResultAsync(services, tenantId, document, staged.Pivot!, staged.Json!, result, facturX, logger, cancellationToken);
    }

    /// <summary>Retente un document <c>TechnicalError</c> : anti-doublon d'abord, puis TechnicalError → ReadyToSend → Sending → envoi.</summary>
    private static async Task<SendOutcome> RetryTechnicalErrorAsync(
        IServiceProvider services,
        IPaClient paClient,
        PaAccountDto account,
        TimeProvider timeProvider,
        string tenantId,
        Guid companyId,
        TenantProfileDto? tenantProfile,
        FiscalSettingsDto? fiscalSettings,
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

        var staged = await ReadStagedPivotAsync(services, tenantId, companyId, tenantProfile, fiscalSettings, document, logger, cancellationToken);
        if (staged.Status == StagedReadStatus.NotStaged)
        {
            return SendOutcome.Deferred;
        }

        if (staged.Status == StagedReadStatus.Integrity)
        {
            // Le document n'est plus Detected : la machine à états n'offre AUCUNE cible légale « intégrité »
            // depuis ReadyToSend/Sending/TechnicalError (F06 §3 / TRK02). On NE transitionne PAS (on n'invente
            // pas d'état) ; on consigne une erreur opérateur et on N'ENVOIE JAMAIS un contenu altéré
            // (« bloquer plutôt qu'envoyer faux »). Le document est ré-examiné chaque cycle jusqu'à ré-extraction.
            LogStagingIntegrityNotSent(logger, documentId, tenantId);
            return SendOutcome.Failed;
        }

        if (staged.Status is StagedReadStatus.EmitterUnresolved or StagedReadStatus.TvaUnresolved)
        {
            // Émetteur non résolu (profil vidé entre CHECK et SEND, RB9) OU catégorie TVA non reposée (table de
            // mapping modifiée depuis le CHECK) : HOLD — aucune transmission ni archive. Différé : repris dès que
            // le profil / la table sont rétablis (la cause précise est déjà journalisée dans ReadStagedPivotAsync).
            return SendOutcome.Deferred;
        }

        if (IsUnsendableCreditNote(staged.Pivot!, paClient))
        {
            // Avoir vers une PA sans capacité avoirs : laissé en TechnicalError (aucune transition), traité par
            // le pipeline des avoirs (PIP02) — jamais promu en ReadyToSend (pas de double comptage ni de boucle).
            LogCreditNoteCapabilityMissing(logger, documentId, paClient.Capabilities.PaName);
            return SendOutcome.Skipped;
        }

        // Garde autofacturation 389 (MND07) AVANT toute reprise/transition : un self-billed sans capacité PA
        // ou sans BT-1 fiscal alloué reste en TechnicalError (jamais re-promu ni émis faux), comme l'avoir
        // sans capacité — pas de double comptage ni de boucle.
        var selfBilled = await ResolveSelfBilledSendAsync(services, paClient, companyId, document.Id, staged.Pivot!, logger, cancellationToken);
        if (selfBilled.Hold)
        {
            return SendOutcome.Skipped;
        }

        var lifecycle = services.GetRequiredService<IDocumentLifecycle>();

        // Reprise : TechnicalError → ReadyToSend (version de mapping déjà posée au CHECK, on la reconsigne).
        await lifecycle.MarkReadyToSendAsync(documentId, document.MappingVersion!, cancellationToken);

        // Anti-doublon AVANT tout renvoi : si la PA connaît déjà le document, on le finalise sans réémettre.
        if (await TryFinalizeFromPaStatusAsync(services, paClient, tenantId, document, staged.Pivot!, staged.Json!, beginSending: true, logger, cancellationToken))
        {
            return SendOutcome.Succeeded;
        }

        // Même garde anti double-envoi que RecoverSendingAsync : si la transmission est déjà journalisée, on
        // finalise sans retransmettre. beginSending:true car le document vient de repasser ReadyToSend.
        if (await TryFinalizeFromJournalAsync(services, tenantId, document, staged.Pivot!, staged.Json!, beginSending: true, logger, cancellationToken))
        {
            return SendOutcome.Succeeded;
        }

        // Anti double-dépôt ASYNCHRONE (item PIPE01) — invariant garanti en CHAQUE point de (re)transmission, pas
        // seulement dans RecoverSendingAsync : un document portant DÉJÀ une référence PA (flux déposé sur une PA
        // asynchrone) n'est JAMAIS re-déposé, même depuis le chemin TechnicalError. DÉFENSIF : ce cas est
        // normalement inatteignable (un dépôt async accepté reste Sending, jamais TechnicalError — le chemin
        // recovery ne transmet plus), mais on ne PRÉSUME pas l'inatteignabilité d'un invariant fiscal P1 (un
        // renvoi = nouveau flux Chorus Pro = double déclaration, CLAUDE.md n°3). L'anti-doublon Issued par
        // référence est déjà couvert ci-dessus (TryFinalizeFromPaStatusAsync) ; ici on MAINTIENT sans re-déposer.
        if (!string.IsNullOrWhiteSpace(document.PaDocumentId))
        {
            LogAsyncReferenceStillPending(logger, document.Id, PaSendState.TechnicalError);
            return SendOutcome.Deferred;
        }

        await lifecycle.BeginSendingAsync(documentId, cancellationToken);
        var (result, facturX) = await TransmitAsync(services, paClient, account, timeProvider, staged.Pivot!, selfBilled.Projection, cancellationToken);
        return await HandleSendResultAsync(services, tenantId, document, staged.Pivot!, staged.Json!, result, facturX, logger, cancellationToken);
    }

    /// <summary>Envoie un document <c>ReadyToSend</c> : ReadyToSend → Sending → envoi → issue.</summary>
    private static async Task<SendOutcome> SendReadyAsync(
        IServiceProvider services,
        IPaClient paClient,
        PaAccountDto account,
        TimeProvider timeProvider,
        string tenantId,
        Guid companyId,
        TenantProfileDto? tenantProfile,
        FiscalSettingsDto? fiscalSettings,
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

        var staged = await ReadStagedPivotAsync(services, tenantId, companyId, tenantProfile, fiscalSettings, document, logger, cancellationToken);
        if (staged.Status == StagedReadStatus.NotStaged)
        {
            return SendOutcome.Deferred;
        }

        if (staged.Status == StagedReadStatus.Integrity)
        {
            // Le document n'est plus Detected : la machine à états n'offre AUCUNE cible légale « intégrité »
            // depuis ReadyToSend/Sending/TechnicalError (F06 §3 / TRK02). On NE transitionne PAS (on n'invente
            // pas d'état) ; on consigne une erreur opérateur et on N'ENVOIE JAMAIS un contenu altéré
            // (« bloquer plutôt qu'envoyer faux »). Le document est ré-examiné chaque cycle jusqu'à ré-extraction.
            LogStagingIntegrityNotSent(logger, documentId, tenantId);
            return SendOutcome.Failed;
        }

        if (staged.Status is StagedReadStatus.EmitterUnresolved or StagedReadStatus.TvaUnresolved)
        {
            // Émetteur non résolu (profil vidé entre CHECK et SEND, RB9) OU catégorie TVA non reposée (table de
            // mapping modifiée depuis le CHECK) : HOLD — aucune transmission ni archive. Différé : repris dès que
            // le profil / la table sont rétablis (la cause précise est déjà journalisée dans ReadStagedPivotAsync).
            return SendOutcome.Deferred;
        }

        // Garde-fou avoirs : un avoir vers une PA sans capacité avoirs reste ReadyToSend (traité par PIP02),
        // jamais bloqué ni envoyé à l'aveugle (l'état machine interdit un retour Sending → ReadyToSend).
        if (IsUnsendableCreditNote(staged.Pivot!, paClient))
        {
            LogCreditNoteCapabilityMissing(logger, documentId, paClient.Capabilities.PaName);
            return SendOutcome.Skipped;
        }

        // Garde autofacturation 389 (MND07) : self-billed sans capacité PA ou sans BT-1 fiscal alloué (MND05)
        // → maintenu ReadyToSend (jamais émis faux ni dégradé en facture standard — CLAUDE.md n°3/8).
        var selfBilled = await ResolveSelfBilledSendAsync(services, paClient, companyId, document.Id, staged.Pivot!, logger, cancellationToken);
        if (selfBilled.Hold)
        {
            return SendOutcome.Skipped;
        }

        await services.GetRequiredService<IDocumentLifecycle>().BeginSendingAsync(documentId, cancellationToken);
        var (result, facturX) = await TransmitAsync(services, paClient, account, timeProvider, staged.Pivot!, selfBilled.Projection, cancellationToken);
        return await HandleSendResultAsync(services, tenantId, document, staged.Pivot!, staged.Json!, result, facturX, logger, cancellationToken);
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

        // Finalisation ANTI-DOUBLON : la PA connaissait déjà le document — AUCUNE transmission n'a eu lieu
        // ce cycle, donc aucun artefact transmis à journaliser (facturX null). L'éventuelle journalisation
        // FX07 a déjà été posée lors de la transmission d'origine.
        await FinalizeIssuedAsync(services, tenantId, document, pivot, canonicalJson, paResponseJson, status.PaDocumentId, facturX: null, cancellationToken);
        LogAntiDuplicateFinalized(logger, document.Id);
        return true;
    }

    /// <summary>
    /// Raccrochage AUTORITAIRE d'un document déjà déposé sur une PA ASYNCHRONE (item PIPE01, D7) : le document
    /// porte une référence PA (n° de flux) persistée à l'accusé de réception. On RELIT le statut par cette
    /// référence (<see cref="IPaClient.GetDocumentStatusAsync"/>) et on finalise sur un état TERMINAL —
    /// <c>Issued</c> (émission confirmée) ou <c>RejectedByPa</c> (rejet) — sinon on MAINTIENT le document
    /// <c>Sending</c> (différé, repris au prochain cycle). On ne RE-DÉPOSE JAMAIS le flux (anti double-dépôt,
    /// CLAUDE.md n°3) : une PA asynchrone (Chorus Pro) crée un nouveau flux à chaque dépôt, donc un renvoi
    /// serait une double déclaration fiscale. La référence n'étant relue qu'en lecture, un statut indisponible
    /// (technique/capacité absente) MAINTIENT Sending plutôt que d'inventer une issue.
    /// </summary>
    private static async Task<SendOutcome> FinalizeFromAsyncPaReferenceAsync(
        IServiceProvider services,
        IPaClient paClient,
        string tenantId,
        DocumentDto document,
        PivotDocumentDto pivot,
        string canonicalJson,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var status = await paClient.GetDocumentStatusAsync(document.PaDocumentId!, cancellationToken);
        switch (status.State)
        {
            case PaSendState.Issued:
                // Émission CONFIRMÉE par la PA : finalisation (archive WORM + purge). Aucune transmission ce
                // cycle (facturX null) — la PA connaissait déjà le document ; preuve = réponse de statut relue.
                await FinalizeIssuedAsync(services, tenantId, document, pivot, canonicalJson, SendPaSnapshot.FromStatus(status), status.PaDocumentId, facturX: null, cancellationToken);
                LogAsyncReferenceConfirmedIssued(logger, document.Id);
                return SendOutcome.Succeeded;

            case PaSendState.RejectedByPa:
                // Rejet CONFIRMÉ par la PA : staging CONSERVÉ (contenu requis pour correction/resoumission —
                // ADR-0014, jamais en WORM). Snapshots de la tentative (payload transmis + réponse de rejet).
                await services.GetRequiredService<IDocumentLifecycle>().MarkRejectedByPaAsync(
                    document.Id,
                    new DocumentRejectionSnapshots { PayloadSnapshot = canonicalJson, PaResponseSnapshot = SendPaSnapshot.FromStatus(status) },
                    cancellationToken);
                LogAsyncReferenceConfirmedRejected(logger, document.Id);
                return SendOutcome.Failed;

            default:
                // Encore en traitement (Sending/New) OU statut indisponible (TechnicalError/CapabilityNotSupported,
                // erreur de lecture transitoire) : on MAINTIENT Sending et on ne RE-DÉPOSE JAMAIS (anti double-dépôt).
                // Repris au prochain cycle — jamais un faux échec ni une issue inventée sur une facture acceptée.
                LogAsyncReferenceStillPending(logger, document.Id, status.State);
                return SendOutcome.Deferred;
        }
    }

    /// <summary>
    /// Garde anti double-envoi par la PISTE D'AUDIT : si une transmission est DÉJÀ journalisée pour ce document
    /// (clé d'idempotence = numéro de document, FX06/FX07), elle a effectivement eu lieu à un cycle précédent
    /// (crash avant <c>MarkIssued</c>, document resté <c>Sending</c>) — on finalise <c>Issued</c> SANS
    /// retransmettre (jamais de double émission, INV-PIPELINE-016/041). Retourne <c>false</c> si rien n'est
    /// journalisé (cas nominal : on transmet). Auto-gating : les PA de niveau Pilotage ne journalisent pas
    /// (artefact nul) ⇒ toujours <c>false</c> pour elles (leur filet reste le dédoublonnage PA par numéro).
    /// <paramref name="beginSending"/> engage la transition Sending requise avant la finalisation (cas retry
    /// depuis ReadyToSend).
    /// </summary>
    private static async Task<bool> TryFinalizeFromJournalAsync(
        IServiceProvider services,
        string tenantId,
        DocumentDto document,
        PivotDocumentDto pivot,
        string canonicalJson,
        bool beginSending,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var journaled = await services.GetRequiredService<IPaTransmissionJournalQueries>()
            .FindByIdempotencyKeyAsync(document.DocumentNumber, cancellationToken);
        if (journaled is null)
        {
            return false;
        }

        if (beginSending)
        {
            await services.GetRequiredService<IDocumentLifecycle>().BeginSendingAsync(document.Id, cancellationToken);
        }

        // Réponse PA d'origine non relisible (perdue au crash) : snapshot HONNÊTE de raccrochage (jamais une
        // réponse PA inventée), traçant la preuve de transmission journalisée.
        var recoveredSnapshot = System.Text.Json.JsonSerializer.Serialize(new
        {
            recovered = "Transmission déjà journalisée à un cycle précédent — raccrochage anti double-envoi (FX07).",
            idempotencyKey = journaled.IdempotencyKey,
            transmittedArtifactHash = journaled.TransmittedArtifactHash,
            paAccount = journaled.PaAccount,
            paPluginId = journaled.PaPluginId,
            paResponseUtc = journaled.PaResponseUtc,
        });

        // facturX null : AUCUNE retransmission ce cycle ⇒ aucune nouvelle journalisation/trace (déjà posées à
        // la transmission d'origine). paDocumentId null : la PA générique (Essentiel) n'a pas de référence
        // relisible ; la preuve d'émission est l'archive WORM + la journalisation FX06.
        await FinalizeIssuedAsync(services, tenantId, document, pivot, canonicalJson, recoveredSnapshot, paDocumentId: null, facturX: null, cancellationToken);
        LogAntiDuplicateJournalFinalized(logger, document.Id);
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
        FacturXTransmission? facturX,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var lifecycle = services.GetRequiredService<IDocumentLifecycle>();
        switch (result.State)
        {
            case PaSendState.Issued:
                // facturX non nul UNIQUEMENT si la PA active a généré+transmis un Factur-X (capacité
                // SupportsFacturXTransmission) : la journalisation FX07 + la trace de support n'ont lieu que
                // sur une transmission RÉUSSIE (Issued), jamais sur un rejet/erreur (pas d'artefact « transmis »).
                await FinalizeIssuedAsync(services, tenantId, document, pivot, canonicalJson, SendPaSnapshot.FromSendResult(result), result.PaDocumentId, facturX, cancellationToken);
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

            case PaSendState.CapabilityNotSupported:
                // Une PA qui DÉCLARE une capacité (ex. SupportsSelfBilling) mais dont le sérialiseur la REFUSE
                // (incohérence capacité⇔implémentation du plug-in) : refus DÉFINITIF, jamais une boucle de retry
                // (TechnicalError se re-tenterait indéfiniment, ré-émission → refus → boucle). Traité comme un
                // rejet (staging CONSERVÉ pour diagnostic/correction), avec un log dédié — jamais émis faux ni
                // dégradé en facture standard (CLAUDE.md n°3/8). Cas normalement écarté AVANT l'envoi (la garde
                // de capacité maintient le document) ; ce filet couvre une PA incohérente.
                await lifecycle.MarkRejectedByPaAsync(
                    document.Id,
                    new DocumentRejectionSnapshots { PayloadSnapshot = canonicalJson, PaResponseSnapshot = SendPaSnapshot.FromSendResult(result) },
                    cancellationToken);
                LogCapabilityRefusedAtSend(logger, document.Id, result.CapabilityNotSupported?.Capability.ToString() ?? "(inconnue)");
                return SendOutcome.Failed;

            case PaSendState.Sending:
                // PA ASYNCHRONE (SuperPDP F14 §3.4 ; Chorus Pro D7) : un dépôt accepté = facture TÉLÉVERSÉE,
                // pas encore émise (la confirmation suit, différée). Le document RESTE Sending (déjà engagé) —
                // NI succès NI échec. Si la PA a renvoyé une RÉFÉRENCE de flux, on la PERSISTE (item PIPE01) :
                // le raccrochage (RecoverSendingAsync) interrogera la PA par cette référence et NE re-déposera
                // JAMAIS ce flux — une PA asynchrone comme Chorus Pro crée un NOUVEAU flux à chaque dépôt, donc
                // un renvoi serait un DOUBLE DÉPÔT (faute fiscale, CLAUDE.md n°3). JAMAIS une erreur technique,
                // qui afficherait un FAUX échec opérateur sur une facture pourtant acceptée par la PA.
                if (!string.IsNullOrWhiteSpace(result.PaDocumentId))
                {
                    // La réponse brute de l'accusé de dépôt (result.RawResponse) est conservée dans la piste
                    // d'audit : seule preuve que la PA a accepté le dépôt avant l'émission différée.
                    await lifecycle.RecordPaSendingReferenceAsync(document.Id, result.PaDocumentId!, result.RawResponse, cancellationToken);
                }

                LogSendingInProgress(logger, document.Id);
                return SendOutcome.Deferred;

            default:
                // New inattendu pour un envoi de document : on retombe sur une erreur technique re-tentable
                // plutôt que d'inventer une issue ou de laisser le document figé en Sending.
                await lifecycle.MarkTechnicalErrorAsync(document.Id, cancellationToken);
                LogUnexpectedSendState(logger, document.Id, result.State.ToString());
                return SendOutcome.Failed;
        }
    }

    /// <summary>
    /// Émission : (1) si chemin Factur-X — journalisation de l'envoi PA (F06 APPEND-ONLY) + trace de support,
    /// posés AVANT MarkIssued (ancre d'idempotence + aucun fait d'audit perdu si crash) ; (2) archive WORM
    /// (TRK05, idempotente) ; (3) MarkIssued (preuve) ; (4) purge du staging subordonnée au WORM (ADR-0014 §4).
    /// Ordre auto-cicatrisant : un crash avant MarkIssued laisse le document Sending — si journal+trace ont été
    /// posés, la garde anti double-envoi (INV-PIPELINE-041) raccrocherait proprement au cycle suivant.
    /// <paramref name="facturX"/> non nul ⇔ un Factur-X a été GÉNÉRÉ et TRANSMIS ce cycle (PA à capacité
    /// <c>SupportsFacturXTransmission</c>) : journal et trace n'ont lieu que dans cette branche (F16 §7) ; les
    /// PA de niveau Pilotage le laissent nul et leur chemin Archive→MarkIssued→purge reste INCHANGÉ. Aucun
    /// chemin update/delete sur document_events (CLAUDE.md n°4).
    /// </summary>
    private static async Task FinalizeIssuedAsync(
        IServiceProvider services,
        string tenantId,
        DocumentDto document,
        PivotDocumentDto pivot,
        string canonicalJson,
        string paResponseJson,
        string? paDocumentId,
        FacturXTransmission? facturX,
        CancellationToken cancellationToken)
    {
        var mappingTraceJson = System.Text.Json.JsonSerializer.Serialize(
            new { mappingVersion = document.MappingVersion ?? "(non précisée)" });

        // FX07 (F16 §7) : sur le chemin Factur-X, journaliser l'envoi (piste d'audit F06 APPEND-ONLY :
        // compte/plug-in PA, horodatages, empreinte de l'artefact, clé d'idempotence, réponse PA) PUIS écrire
        // la trace de support (copie du Factur-X transmis, store DÉDIÉ purgeable, tenant-scopé n°9 — distinct
        // de l'audit WORM). Posés AVANT MarkIssued : la clé est l'ancre d'idempotence pour la garde anti
        // double-envoi (INV-PIPELINE-041) ; un crash ici laisse le document Sending (ré-examiné au cycle
        // suivant), le fait d'audit est conservé. Aucun chemin update/delete sur document_events (CLAUDE.md n°4).
        if (facturX is not null)
        {
            await services.GetRequiredService<IPaTransmissionJournal>().JournalAsync(
                new PaTransmissionJournalEntry
                {
                    DocumentId = document.Id,
                    PaAccount = facturX.PaAccount,
                    PaPluginId = facturX.PaPluginId,
                    PaRequestUtc = facturX.RequestUtc,
                    PaResponseUtc = facturX.ResponseUtc,
                    TransmittedArtifactHash = facturX.ArtifactHash,
                    IdempotencyKey = document.DocumentNumber,
                    PaResponseSnapshot = paResponseJson,
                    Detail = string.Create(
                        CultureInfo.InvariantCulture,
                        $"Factur-X transmis à la Plateforme Agréée (compte « {facturX.PaAccount} », plug-in « {facturX.PaPluginId} »)."),
                },
                cancellationToken);

            await services.GetRequiredService<ISupportTraceStore>().WriteAsync(
                tenantId, document.Id, facturX.Artifact, facturX.ResponseUtc, cancellationToken);
        }

        var archiveRequest = SendArchiveComposer.Compose(document, pivot, canonicalJson, paResponseJson, mappingTraceJson);
        await services.GetRequiredService<IArchiveService>().ArchiveIssuedDocumentAsync(archiveRequest, cancellationToken);

        // La référence PA est persistée sur le document à l'émission (clé de récupération aval — SYNC/PIP01d) ;
        // elle n'est jamais effacée par une finalisation anti-doublon sans id (Document.MarkIssued).
        await services.GetRequiredService<IDocumentLifecycle>().MarkIssuedAsync(
            document.Id,
            new DocumentIssuanceSnapshots
            {
                PayloadSnapshot = canonicalJson,
                PaResponseSnapshot = paResponseJson,
                MappingTrace = mappingTraceJson,
                PaDocumentId = paDocumentId,
            },
            cancellationToken);

        // Purge subordonnée à la présence EFFECTIVE du paquet WORM (jamais à la seule étiquette Issued).
        var key = new StagedPayloadKey(tenantId, document.Id, document.PayloadHash);
        var locator = new ArchivedDocumentLocator(document.Id, document.IssueDate.Year, document.IssueDate.Month, document.DocumentNumber);
        await services.GetRequiredService<IStagingPurgeService>().PurgeIfArchivedAsync(key, locator, cancellationToken);
    }

    /// <summary>
    /// Transmet le pivot à la PA active à l'étape <c>Sending</c> (F16 §6.1) : si la PA déclare
    /// <c>SupportsFacturXTransmission</c> (jamais <c>if (pa is …)</c>, CLAUDE.md n°8), GÉNÈRE le Factur-X
    /// scellé JUSTE AVANT l'appel de transmission (via le pont <see cref="IFacturXArtifactBuilder"/> résolu
    /// au Host, qui délègue à <c>IFacturXBuilder</c>) et le passe au plug-in dans le <see cref="PaSendContext"/> ;
    /// les PA de niveau Pilotage ne génèrent rien (contexte nul, chemin inchangé). Capture les horodatages
    /// requête/réponse et l'empreinte de l'artefact pour la journalisation FX07 (renvoyés via
    /// <see cref="FacturXTransmission"/>, non nul uniquement sur le chemin Factur-X).
    /// </summary>
    private static async Task<(PaSendResult Result, FacturXTransmission? FacturX)> TransmitAsync(
        IServiceProvider services,
        IPaClient paClient,
        PaAccountDto account,
        TimeProvider timeProvider,
        PivotDocumentDto pivot,
        PaOutboundProjection? projection,
        CancellationToken cancellationToken)
    {
        PaSendContext? context = null;
        ReadOnlyMemory<byte> artifact = default;

        if (paClient.Capabilities.SupportsFacturXTransmission)
        {
            // Génération déterministe du pivot SEUL, AVANT la transmission (jamais dans FinalizeIssuedAsync —
            // qui s'exécute en aval, après le retour Issued : y générer produirait l'artefact APRÈS l'envoi,
            // faux-vert F16 §6.1/§7). Bloque (lève) si un BT obligatoire manque (ADR-0023 INV-FX-2).
            artifact = await services.GetRequiredService<IFacturXArtifactBuilder>()
                .BuildSealedArtifactAsync(pivot, cancellationToken);
            context = new PaSendContext(artifact);
        }

        var requestUtc = timeProvider.GetUtcNow();
        var result = await paClient.SendDocumentAsync(pivot, sendAfterImport: true, projection: projection, context: context, cancellationToken: cancellationToken);
        var responseUtc = timeProvider.GetUtcNow();

        FacturXTransmission? facturX = artifact.IsEmpty
            ? null
            : new FacturXTransmission(
                artifact,
                ComputeSha256Hex(artifact.Span),
                requestUtc,
                responseUtc,
                account.AccountIdentifiers,
                account.PluginType);

        return (result, facturX);
    }

    /// <summary>Empreinte SHA-256 (hex minuscule, préfixe <c>sha256:</c>) de l'artefact transmis — pour la piste d'audit FX07.</summary>
    private static string ComputeSha256Hex(ReadOnlySpan<byte> bytes)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(bytes, hash);
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Relit le pivot stagé (PIP00) et re-vérifie l'intégrité. Absence = transitoire ; altération = à bloquer.</summary>
    private static async Task<StagedRead> ReadStagedPivotAsync(
        IServiceProvider services,
        string tenantId,
        Guid companyId,
        TenantProfileDto? tenantProfile,
        FiscalSettingsDto? fiscalSettings,
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

            // Émetteur rempli au READ-TIME depuis le profil tenant (ADR-0023 amendé / RB9) : le blob stagé est le
            // pivot SOURCE (hashé à l'ingestion pour l'anti-doublon F06). On l'enrichit ICI pour l'émission, et on
            // RE-SÉRIALISE l'enrichi pour que l'archive WORM porte EXACTEMENT ce qui est émis. Le profil/fiscal sont
            // résolus UNE fois en tête de job (ExecuteAsync) et propagés — aucune relecture tenant par document.
            var enriched = PivotEmitterEnricher.Enrich(pivot, tenantProfile, fiscalSettings);

            // Garde anti-« envoi faux » (RB9) : si le profil tenant a perdu son SIREN entre le CHECK (passé,
            // émetteur rempli) et le SEND, l'émetteur redevient nul. On NE transmet PAS un document sans
            // émetteur (BT-27/BT-31 obligatoires) — HOLD, repris dès que le profil est rétabli (CLAUDE.md n°3).
            // Sans cette garde, le chemin non-Factur-X transmettrait sans vendeur puis l'archive lèverait
            // (NRE sur Supplier) APRÈS l'envoi : exactement l'inversion à proscrire.
            if (enriched.Supplier is null)
            {
                LogEmitterUnresolvedNotSent(logger, document.Id, document.DocumentNumber, tenantId);
                return new StagedRead(StagedReadStatus.EmitterUnresolved, null, null);
            }

            // Mapping TVA rempli au READ-TIME (catégorie UNCL5305 + VATEX par ligne) — SYMÉTRIQUE à l'émetteur
            // (emitter-filled-by-platform / ADR-0023 amendé) : le blob stagé est le pivot SOURCE (régimes bruts,
            // catégorie nulle — hashé à l'ingestion pour l'anti-doublon F06). La PA exige la catégorie par ligne
            // (EN 16931 BG-30) : on la repose ICI, depuis la table validée du tenant, via le MÊME moteur qu'au
            // CHECK (CheckTvaMapping) — une seule source de la classification, jamais inventée (F03). Un régime
            // devenu non couvert entre CHECK et SEND (table modifiée) → HOLD différé, jamais un envoi sans catégorie.
            var mappingPlan = CheckTvaMapping.BuildPlan(enriched);
            if (mappingPlan.Requests.Count > 0)
            {
                var mappingResult = await services.GetRequiredService<ITvaMappingService>()
                    .MapAsync(companyId, mappingPlan.Requests, cancellationToken);
                var evaluation = CheckTvaMapping.Evaluate(enriched, mappingPlan, mappingResult);
                if (evaluation.IsBlocked)
                {
                    LogTvaUnresolvedNotSent(logger, document.Id, document.DocumentNumber, tenantId);
                    return new StagedRead(StagedReadStatus.TvaUnresolved, null, null);
                }

                enriched = evaluation.EnrichedDocument!;
            }

            if (!ReferenceEquals(enriched, pivot))
            {
                pivot = enriched;
                canonicalJson = CanonicalJson.Serialize(enriched);
            }

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
    /// <remarks>Délègue à <see cref="PaTaxReportSetting.IsActiveOn"/> (source UNIQUE de la règle) : l'état
    /// affiché par la console (FIX201) et ce gating d'envoi ne peuvent ainsi jamais diverger.</remarks>
    private static bool IsTaxReportSettingActive(PaTaxReportSetting setting, TimeProvider timeProvider) =>
        setting.IsActiveOn(DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime));

    private static bool IsUnsendableCreditNote(PivotDocumentDto pivot, IPaClient paClient) =>
        pivot.CreditNoteRefs.Count > 0 && !paClient.Capabilities.SupportsCreditNotes;

    /// <summary>
    /// Résout l'envoi d'un document self-billed (autofacturation 389, MND07) : lit l'acceptation (MND02/03)
    /// pour récupérer le BT-1 fiscal alloué (MND05) et confirme la capacité PA. Renvoie un HOLD (document
    /// maintenu, jamais émis faux — CLAUDE.md n°3/8) quand la PA active ne déclare pas
    /// <c>SupportsSelfBilling</c>, que l'acceptation n'est pas acquise, ou que le BT-1 fiscal n'est pas
    /// encore alloué. Un document NON self-billed renvoie une projection nulle (le plug-in conserve son
    /// comportement standard, type 380, BT-1 = <c>Number</c> du pivot). La garde PRIMAIRE est au CHECK
    /// (<see cref="Check.DocumentCheckEvaluator"/>) ; ce filet couvre un changement de PA/d'acceptation
    /// survenu APRÈS le passage ReadyToSend.
    /// </summary>
    private static async Task<SelfBilledSend> ResolveSelfBilledSendAsync(
        IServiceProvider services,
        IPaClient paClient,
        Guid companyId,
        Guid documentId,
        PivotDocumentDto pivot,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!pivot.IsSelfBilled)
        {
            return SelfBilledSend.Standard;
        }

        if (!paClient.Capabilities.SupportsSelfBilling)
        {
            LogSelfBilledCapabilityMissing(logger, documentId, paClient.Capabilities.PaName);
            return SelfBilledSend.Held;
        }

        var acceptance = await services.GetRequiredService<ISelfBilledAcceptanceQueries>()
            .GetAcceptance(companyId, documentId, cancellationToken);
        if (acceptance is null || !acceptance.IsAccepted)
        {
            LogSelfBilledNotAccepted(logger, documentId);
            return SelfBilledSend.Held;
        }

        if (string.IsNullOrWhiteSpace(acceptance.AllocatedNumber))
        {
            LogSelfBilledNumberNotAllocated(logger, documentId);
            return SelfBilledSend.Held;
        }

        return SelfBilledSend.Emit(PaOutboundProjection.ForSelfBilled(acceptance.AllocatedNumber));
    }

    private static async Task<SendOutcome> SafeProcessAsync(
        Func<Task<SendOutcome>> process,
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
            LogDocumentSendFailed(logger, documentId, ex);
            return SendOutcome.Failed;
        }
    }

    /// <summary>
    /// Capture l'instantané des identifiants candidats AVANT tout traitement (ensemble stable → pagination OFFSET
    /// fidèle), puis ils sont traités ; un document qui quitte l'état pendant le traitement n'est donc jamais sauté
    /// (même garantie que GetPotentiallySentDocumentsAsync).
    /// </summary>
    private static async Task<List<Guid>> SnapshotIdsByStateAsync(
        IServiceProvider services,
        string state,
        CancellationToken cancellationToken)
    {
        var queries = services.GetRequiredService<IDocumentQueries>();
        var ids = new List<Guid>();
        var page = 1;
        while (true)
        {
            var batch = await queries.GetByStateAsync(state, page, PageSize, cancellationToken);
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

    private static async Task ForEachByStateAsync(
        IServiceProvider services,
        string state,
        Func<Guid, Task> action,
        CancellationToken cancellationToken)
    {
        var ids = await SnapshotIdsByStateAsync(services, state, cancellationToken);
        foreach (var id in ids)
        {
            await action(id);
        }
    }

    private static async Task<int> CountByStateAsync(
        IServiceProvider services,
        string state,
        CancellationToken cancellationToken)
    {
        var ids = await SnapshotIdsByStateAsync(services, state, cancellationToken);
        return ids.Count;
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

    [LoggerMessage(EventId = 7220, Level = LogLevel.Information,
        Message = "SEND : document {DocumentId} téléversé à la Plateforme Agréée (émission asynchrone en cours) — maintenu « en cours d'envoi », finalisé automatiquement dès l'émission confirmée par la PA.")]
    private static partial void LogSendingInProgress(ILogger logger, Guid documentId);

    [LoggerMessage(EventId = 7221, Level = LogLevel.Information,
        Message = "SEND : document {DocumentId} confirmé Issued par la Plateforme Agréée (dépôt asynchrone, relecture par référence de flux) — finalisé sans renvoi (anti double-dépôt PIPE01).")]
    private static partial void LogAsyncReferenceConfirmedIssued(ILogger logger, Guid documentId);

    [LoggerMessage(EventId = 7222, Level = LogLevel.Warning,
        Message = "SEND : document {DocumentId} rejeté par la Plateforme Agréée (dépôt asynchrone, relecture par référence de flux) — RejectedByPa, staging conservé (anti double-dépôt PIPE01).")]
    private static partial void LogAsyncReferenceConfirmedRejected(ILogger logger, Guid documentId);

    [LoggerMessage(EventId = 7223, Level = LogLevel.Information,
        Message = "SEND : document {DocumentId} encore en traitement côté Plateforme Agréée (dépôt asynchrone, statut {State}) — maintenu Sending, jamais re-déposé (anti double-dépôt PIPE01).")]
    private static partial void LogAsyncReferenceStillPending(ILogger logger, Guid documentId, PaSendState state);

    [LoggerMessage(EventId = 7209, Level = LogLevel.Warning,
        Message = "SEND : avoir {DocumentId} non envoyé — la Plateforme Agréée « {PaName} » ne déclare pas la capacité avoirs (maintenu ReadyToSend, traité par le pipeline des avoirs).")]
    private static partial void LogCreditNoteCapabilityMissing(ILogger logger, Guid documentId, string paName);

    [LoggerMessage(EventId = 7210, Level = LogLevel.Warning,
        Message = "SEND : document {DocumentId} sans version de mapping en envoi — anomalie de données, non renvoyé.")]
    private static partial void LogMissingMappingVersion(ILogger logger, Guid documentId);

    [LoggerMessage(EventId = 7211, Level = LogLevel.Error,
        Message = "SEND : contenu stagé altéré/illisible pour le document {DocumentId} (tenant « {TenantId} ») — NON envoyé (intégrité) ; ré-extraction requise. Aucune transition d'état (document hors Detected).")]
    private static partial void LogStagingIntegrityNotSent(ILogger logger, Guid documentId, string tenantId);

    [LoggerMessage(EventId = 7218, Level = LogLevel.Warning,
        Message = "SEND : émetteur non résolu pour le document {DocumentNumber} ({DocumentId}, tenant « {TenantId} ») — le profil tenant n'a pas (ou plus) de SIREN. Document NON transmis (HOLD, repris automatiquement au prochain cycle). Action opérateur : renseignez le SIREN et la raison sociale dans Paramétrage › Profil de l'entreprise.")]
    private static partial void LogEmitterUnresolvedNotSent(ILogger logger, Guid documentId, string documentNumber, string tenantId);

    [LoggerMessage(EventId = 7219, Level = LogLevel.Warning,
        Message = "SEND : catégorie de TVA non reposée pour le document {DocumentNumber} ({DocumentId}, tenant « {TenantId} ») — la table de mapping TVA a changé depuis le contrôle (un régime n'est plus couvert). Document NON transmis (HOLD, repris automatiquement au prochain cycle). Action opérateur : complétez/faites valider la table de mapping TVA dans Paramétrage › TVA.")]
    private static partial void LogTvaUnresolvedNotSent(ILogger logger, Guid documentId, string documentNumber, string tenantId);

    [LoggerMessage(EventId = 7212, Level = LogLevel.Error,
        Message = "SEND : échec inattendu sur le document {DocumentId} — document ignoré ce cycle, traitement du tenant poursuivi.")]
    private static partial void LogDocumentSendFailed(ILogger logger, Guid documentId, Exception exception);

    [LoggerMessage(EventId = 7213, Level = LogLevel.Warning,
        Message = "SEND : auto-facture sous mandat (389) {DocumentId} non envoyée — la Plateforme Agréée « {PaName} » ne déclare pas la capacité d'émission 389 (maintenue ; ne sera jamais émise en facture standard).")]
    private static partial void LogSelfBilledCapabilityMissing(ILogger logger, Guid documentId, string paName);

    [LoggerMessage(EventId = 7214, Level = LogLevel.Warning,
        Message = "SEND : auto-facture sous mandat (389) {DocumentId} non envoyée — acceptation par le mandant non acquise (art. 289 I-2 CGI) ; maintenue jusqu'à acceptation.")]
    private static partial void LogSelfBilledNotAccepted(ILogger logger, Guid documentId);

    [LoggerMessage(EventId = 7215, Level = LogLevel.Warning,
        Message = "SEND : auto-facture sous mandat (389) {DocumentId} non envoyée — BT-1 fiscal par mandant non encore alloué (MND05) ; maintenue (jamais émise avec le numéro source en place du BT-1 fiscal).")]
    private static partial void LogSelfBilledNumberNotAllocated(ILogger logger, Guid documentId);

    [LoggerMessage(EventId = 7216, Level = LogLevel.Error,
        Message = "SEND : document {DocumentId} refusé par le plug-in PA pour capacité « {Capability} » alors qu'elle est déclarée (incohérence capacité⇔implémentation) — traité comme rejet définitif, jamais re-tenté en boucle.")]
    private static partial void LogCapabilityRefusedAtSend(ILogger logger, Guid documentId, string capability);

    [LoggerMessage(EventId = 7217, Level = LogLevel.Information,
        Message = "SEND : document {DocumentId} déjà transmis (journalisé) à un cycle précédent — finalisé Issued sans renvoi (anti double-envoi FX07).")]
    private static partial void LogAntiDuplicateJournalFinalized(ILogger logger, Guid documentId);

    /// <summary>Issue de la résolution self-billed (MND07) : émettre (avec/sans projection) ou maintenir (hold).</summary>
    private readonly struct SelfBilledSend
    {
        private SelfBilledSend(bool hold, PaOutboundProjection? projection)
        {
            Hold = hold;
            Projection = projection;
        }

        /// <summary>Document standard : aucune projection (le plug-in projette son type par défaut 380).</summary>
        public static SelfBilledSend Standard => new(hold: false, projection: null);

        /// <summary>Document maintenu : capacité absente, acceptation non acquise ou BT-1 fiscal non alloué.</summary>
        public static SelfBilledSend Held => new(hold: true, projection: null);

        /// <summary>Le document est maintenu (jamais émis ce cycle).</summary>
        public bool Hold { get; }

        /// <summary>Projection sortante à passer au plug-in (389 + BT-1 fiscal), ou <c>null</c> (document standard).</summary>
        public PaOutboundProjection? Projection { get; }

        /// <summary>Émettre avec la projection 389 (type + BT-1 fiscal alloué).</summary>
        public static SelfBilledSend Emit(PaOutboundProjection projection) => new(hold: false, projection: projection);
    }

    /// <summary>
    /// Données capturées d'une transmission Factur-X (FX07) : l'artefact transmis (pour la trace de support),
    /// son empreinte, les horodatages requête/réponse et le compte/plug-in PA — alimentent la journalisation
    /// d'envoi (F06) et la trace de support. Non null uniquement sur le chemin Factur-X (capacité déclarée).
    /// </summary>
    private sealed record FacturXTransmission(
        ReadOnlyMemory<byte> Artifact,
        string ArtifactHash,
        DateTimeOffset RequestUtc,
        DateTimeOffset ResponseUtc,
        string PaAccount,
        string PaPluginId);
}
