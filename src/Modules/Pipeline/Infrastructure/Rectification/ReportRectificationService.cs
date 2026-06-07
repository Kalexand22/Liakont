namespace Liakont.Modules.Pipeline.Infrastructure.Rectification;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Pipeline.Application;
using Liakont.Modules.Pipeline.Domain.Payments;
using Liakont.Modules.Pipeline.Domain.Rectification;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.Transmission.Contracts;
using Microsoft.Extensions.Logging;

/// <summary>
/// Mécanisme de rectification d'e-reporting (PIP04, flux RE annule-et-remplace — F07-F08 §B.1). Pour une
/// période donnée, reconstruit l'agrégat COMPLET corrigé (toutes les lignes reportables de la projection
/// PIP03a, pas un delta — <see cref="RectificationBuilder"/>), applique l'IDEMPOTENCE par empreinte de contenu,
/// transmet le rectificatif via la Plateforme Agréée (PILOTÉ par la capacité <c>SupportsReportRectification</c>,
/// jamais par <c>if (pa is …)</c>) et journalise l'issue APPEND-ONLY (<see cref="IReportRectificationLedger"/>).
/// </summary>
/// <remarks>
/// TENANT-SCOPÉ : la connexion EST le tenant (database-per-tenant). Le service ne fait avancer AUCUNE machine à
/// états de document/agrégat (l'audit de transmission de PIP03b reste à part) ; il consigne ses propres
/// tentatives. Les rectificatifs de PAIEMENT (10.4) ne portent de données réelles qu'une fois PIP03b actif
/// (fenêtrage + enrichissement de <see cref="PaymentReportPeriod"/>) — ici le MÉCANISME est complet et testé
/// avec le plug-in factice. N'écrit PAS de RunLog : la trace d'exécution est portée par l'appelant
/// (<see cref="ReportRectificationTenantJob"/> pour un run tenant).
/// </remarks>
public sealed partial class ReportRectificationService
{
    private readonly IPaymentAggregationStore _aggregations;
    private readonly IReportRectificationLedger _ledger;
    private readonly IPaClientRegistry _registry;
    private readonly ITenantSettingsQueries _tenantSettings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReportRectificationService> _logger;

    /// <summary>Construit le service de rectification (dépendances tenant-scopées).</summary>
    public ReportRectificationService(
        IPaymentAggregationStore aggregations,
        IReportRectificationLedger ledger,
        IPaClientRegistry registry,
        ITenantSettingsQueries tenantSettings,
        TimeProvider timeProvider,
        ILogger<ReportRectificationService> logger)
    {
        _aggregations = aggregations ?? throw new ArgumentNullException(nameof(aggregations));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _tenantSettings = tenantSettings ?? throw new ArgumentNullException(nameof(tenantSettings));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Rectifie la période <paramref name="periodStart"/>..<paramref name="periodEnd"/> (bornes incluses) du
    /// tenant <paramref name="tenantId"/> pour le <paramref name="flux"/> donné : charge la projection
    /// d'agrégation puis délègue à la surcharge qui prend les agrégats en entrée. Pour le rectificatif d'UNE
    /// période (API / opérateur).
    /// </summary>
    public async Task<ReportRectificationOutcome> RectifyPeriodAsync(
        string tenantId,
        PaymentReportFlux flux,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken = default)
    {
        var aggregates = await _aggregations.GetAllAsync(cancellationToken);
        return await RectifyPeriodAsync(tenantId, flux, periodStart, periodEnd, aggregates, cancellationToken);
    }

    /// <summary>
    /// Rectifie une période à partir d'une projection d'agrégation DÉJÀ CHARGÉE : reconstruit l'agrégat complet,
    /// décide de l'idempotence, transmet (ou met en attente faute de capacité), et journalise. Le job de
    /// ré-évaluation charge la projection UNE fois par run et la passe à chaque période (évite O(périodes ×
    /// projection)).
    /// </summary>
    public async Task<ReportRectificationOutcome> RectifyPeriodAsync(
        string tenantId,
        PaymentReportFlux flux,
        DateOnly periodStart,
        DateOnly periodEnd,
        IReadOnlyList<PaymentDailyAggregate> aggregates,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(aggregates);

        var periodLabel = Describe(periodStart, periodEnd);
        var rebuild = RectificationBuilder.Build(periodStart, periodEnd, aggregates);
        var latest = await _ledger.GetLatestAsync(flux, periodStart, periodEnd, cancellationToken);

        // Période sans donnée reportable : AUCUNE transmission. Une période sans encaissement ne donne PAS lieu
        // à une déclaration « néant » par défaut (F09 §5.4, décision ouverte) — on ne transmet jamais une
        // déclaration inventée. Cas « tout corrigé à zéro » APRÈS une déclaration : l'annul total se constate
        // (alerte opérateur, jamais bucketisé en « inchangée »), il ne se sur-déclare pas par un RE vide non sourcé.
        if (rebuild.IsEmpty)
        {
            if (latest is not null)
            {
                LogVoidedAfterDeclaration(_logger, tenantId, periodLabel);
            }

            return new ReportRectificationOutcome
            {
                Decision = ReportRectificationDecision.NothingToDeclare,
                Rectification = rebuild,
                Detail = latest is null
                    ? $"Aucune donnée reportable pour la période {periodLabel} — rien à rectifier."
                    : $"Période {periodLabel} DÉJÀ DÉCLARÉE désormais sans donnée reportable — aucune transmission automatique (déclaration « néant »/annulation non tranchée, F09 §5.4) ; ACTION OPÉRATEUR : statuer sur l'annulation de la période.",
            };
        }

        var paClient = await ResolvePaClientAsync(tenantId, cancellationToken);

        // Transmettre un rectificatif exige DEUX capacités déclarées : la rectification (flux RE) ET le flux
        // d'e-reporting de paiement demandé (10.4 / 10.2). L'absence de l'une OU l'autre ⇒ EN ATTENTE — décidé
        // LOCALEMENT depuis les capacités déclarées (pas d'appel PA inutile, jamais `if (pa is …)`), ce qui rend
        // l'idempotence stable : une période bloquée à contenu inchangé n'est ni renvoyée ni re-journalisée.
        var canTransmit = paClient is not null
            && paClient.Capabilities.SupportsReportRectification
            && paClient.Capabilities.SupportsPaymentReport(flux);

        // IDEMPOTENCE (PIP04 §4) : un contenu déjà transmis (ou déjà en attente faute de capacité, capacités
        // toujours absentes) ne re-transmet pas. Un changement d'empreinte (avoir / altération source) ré-ouvre.
        if (IsIdempotentSkip(latest, rebuild.ContentHash, canTransmit))
        {
            return new ReportRectificationOutcome
            {
                Decision = ReportRectificationDecision.NoChange,
                Rectification = rebuild,
                Detail = NoChangeDetail(latest!.Status, periodLabel),
            };
        }

        var payloadSnapshot = SerializeLines(rebuild.Lines);

        if (!canTransmit)
        {
            var pendingDetail = $"Rectificatif e-reporting EN ATTENTE pour la période {periodLabel} : la Plateforme Agréée ne déclare pas (encore) toutes les capacités requises (rectification flux RE + e-reporting de paiement) — agrégat conservé, transmission automatique dès activation.";
            await AppendEntryAsync(flux, rebuild, ReportRectificationStatus.PendingCapability, paReportId: null, payloadSnapshot, paResponse: null, pendingDetail, cancellationToken);
            LogPending(_logger, tenantId, periodLabel);
            return new ReportRectificationOutcome
            {
                Decision = ReportRectificationDecision.PendingCapability,
                Rectification = rebuild,
                Detail = pendingDetail,
            };
        }

        // TODO(PIP03b) : SendPaymentReportAsync ne porte aujourd'hui que {Flux, bornes}. L'ENRICHISSEMENT des
        // lignes rectifiées (F09 §5.3) ET un MARQUEUR de flux RE (annule-et-remplace, distinct d'une déclaration
        // initiale) sont la responsabilité de PIP03b ; tant que PIP03b est inactif, la PA reçoit le même
        // descripteur qu'une initiale. Le `payload_snapshot` journalisé est la PHOTO de ce qui sera transmis :
        // statut Transmitted ici = MÉCANISME exécuté, contenu réel porté dès PIP03b (INV-PIPELINE-037).
        var period = new PaymentReportPeriod { Flux = flux, PeriodStart = periodStart, PeriodEnd = periodEnd };
        var result = await paClient!.SendPaymentReportAsync(period, cancellationToken);
        var (decision, status, detail) = MapResult(result, periodLabel);

        await AppendEntryAsync(flux, rebuild, status, result.PaDocumentId, payloadSnapshot, result.RawResponse, detail, cancellationToken);
        if (decision == ReportRectificationDecision.Transmitted)
        {
            LogTransmitted(_logger, tenantId, periodLabel, rebuild.Lines.Count);
        }

        return new ReportRectificationOutcome { Decision = decision, Rectification = rebuild, Detail = detail };
    }

    private static bool IsIdempotentSkip(ReportRectificationEntry? latest, string contentHash, bool canTransmit)
    {
        if (latest is null || !string.Equals(latest.ContentHash, contentHash, StringComparison.Ordinal))
        {
            return false;
        }

        // Même contenu que la dernière entrée : on ne re-transmet que si l'état le justifie.
        return latest.Status switch
        {
            // Déjà accepté : annule-et-remplace identique = pas de retransmission (idempotent).
            ReportRectificationStatus.Transmitted => true,

            // Rejet métier identique : pas de retry automatique (l'opérateur doit corriger le contenu).
            ReportRectificationStatus.RejectedByPa => true,

            // En attente de capacité : on re-tente UNIQUEMENT si la transmission a désormais une chance d'aboutir
            // (les DEUX capacités requises présentes) — sinon ni renvoi PA ni doublon de journal append-only.
            ReportRectificationStatus.PendingCapability => !canTransmit,

            // Erreur technique transitoire : on re-tente toujours.
            ReportRectificationStatus.TechnicalError => false,

            _ => false,
        };
    }

    private static (ReportRectificationDecision Decision, ReportRectificationStatus Status, string Detail) MapResult(
        PaSendResult result,
        string periodLabel)
    {
        return result.State switch
        {
            PaSendState.Issued => (
                ReportRectificationDecision.Transmitted,
                ReportRectificationStatus.Transmitted,
                $"Rectificatif e-reporting transmis (annule-et-remplace) pour la période {periodLabel}."),

            PaSendState.RejectedByPa => (
                ReportRectificationDecision.RejectedByPa,
                ReportRectificationStatus.RejectedByPa,
                $"Rectificatif e-reporting REJETÉ par la Plateforme Agréée pour la période {periodLabel} — action opérateur : corrigez les données puis relancez."),

            // DÉFENSIF : les capacités requises sont pré-vérifiées avant l'envoi (canTransmit) ; ce cas ne
            // survient que si une PA déclare la capacité mais la refuse au runtime (incohérence) — repli sûr en attente.
            PaSendState.CapabilityNotSupported => (
                ReportRectificationDecision.PendingCapability,
                ReportRectificationStatus.PendingCapability,
                $"Rectificatif e-reporting EN ATTENTE pour la période {periodLabel} : capacité refusée au runtime par la Plateforme Agréée — transmission automatique dès rétablissement."),

            // Réseau / 5xx / timeout / état inattendu : re-tentable au prochain cycle.
            _ => (
                ReportRectificationDecision.TechnicalError,
                ReportRectificationStatus.TechnicalError,
                $"Rectificatif e-reporting en ERREUR TECHNIQUE pour la période {periodLabel} — re-tentative automatique au prochain cycle."),
        };
    }

    private static string SerializeLines(IReadOnlyList<RectificationLine> lines)
    {
        // Montants/taux en CHAÎNES invariantes dans le jsonb — JAMAIS de float (CLAUDE.md n°1), précision intégrale.
        var payload = lines.Select(line => new LineJson
        {
            Date = line.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Rate = line.Rate.ToString(CultureInfo.InvariantCulture),
            Base = line.TaxableBase.ToString(CultureInfo.InvariantCulture),
            Vat = line.VatAmount.ToString(CultureInfo.InvariantCulture),
        }).ToList();

        return JsonSerializer.Serialize(payload);
    }

    private static string Describe(DateOnly periodStart, DateOnly periodEnd) =>
        $"du {periodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} au {periodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    // Message HONNÊTE du chemin idempotent : selon le DERNIER état, « aucun changement » ne signifie pas
    // « conforme ». Un rejet métier non corrigé ou une attente de capacité ne doivent pas passer pour bénins.
    private static string NoChangeDetail(ReportRectificationStatus latestStatus, string periodLabel) => latestStatus switch
    {
        ReportRectificationStatus.Transmitted =>
            $"Aucun changement depuis la dernière transmission acceptée pour la période {periodLabel} — aucune retransmission.",
        ReportRectificationStatus.RejectedByPa =>
            $"Période {periodLabel} : dernier rectificatif REJETÉ par la Plateforme Agréée, contenu inchangé — ACTION OPÉRATEUR : corrigez les données puis relancez (pas de retransmission automatique d'un contenu identique).",
        ReportRectificationStatus.PendingCapability =>
            $"Période {periodLabel} : toujours EN ATTENTE de la capacité de rectification (flux RE), contenu inchangé — aucun envoi (transmission automatique dès activation).",
        _ =>
            $"Aucun changement pour la période {periodLabel} — aucune retransmission.",
    };

    [LoggerMessage(EventId = 7440, Level = LogLevel.Information,
        Message = "Rectificatif e-reporting transmis pour le tenant « {TenantId} », période {Period} ({Lines} ligne(s), annule-et-remplace).")]
    private static partial void LogTransmitted(ILogger logger, string tenantId, string period, int lines);

    [LoggerMessage(EventId = 7441, Level = LogLevel.Information,
        Message = "Rectificatif e-reporting en attente pour le tenant « {TenantId} », période {Period} : capacité de rectification (flux RE) absente.")]
    private static partial void LogPending(ILogger logger, string tenantId, string period);

    [LoggerMessage(EventId = 7446, Level = LogLevel.Warning,
        Message = "Rectification e-reporting : la période {Period} (tenant « {TenantId} ») DÉJÀ DÉCLARÉE est désormais sans donnée reportable — annulation à statuer par l'opérateur (aucune déclaration « néant » transmise par défaut, F09 §5.4).")]
    private static partial void LogVoidedAfterDeclaration(ILogger logger, string tenantId, string period);

    private async Task<IPaClient?> ResolvePaClientAsync(string tenantId, CancellationToken cancellationToken)
    {
        var companyId = await _tenantSettings.GetCurrentCompanyId(cancellationToken);
        if (companyId is null)
        {
            return null;
        }

        var accounts = await _tenantSettings.GetPaAccounts(companyId.Value, cancellationToken);
        var active = accounts.FirstOrDefault(account => account.IsActive);
        if (active is null)
        {
            return null;
        }

        // Un plug-in non déployé pour le type du compte ⇒ capacité absente (PendingCapability), JAMAIS un échec
        // dur (IsRegistered avant Resolve, qui lèverait sur un type inconnu).
        if (!_registry.IsRegistered(active.PluginType))
        {
            return null;
        }

        return _registry.Resolve(new PaAccountDescriptor(active.PluginType, tenantId));
    }

    private async Task AppendEntryAsync(
        PaymentReportFlux flux,
        ReportRectification rebuild,
        ReportRectificationStatus status,
        string? paReportId,
        string payloadSnapshot,
        string? paResponse,
        string detail,
        CancellationToken cancellationToken)
    {
        await _ledger.AppendAsync(
            new ReportRectificationEntry
            {
                Id = Guid.NewGuid(),
                Flux = flux,
                PeriodStart = rebuild.PeriodStart,
                PeriodEnd = rebuild.PeriodEnd,
                ContentHash = rebuild.ContentHash,
                Status = status,
                PaReportId = paReportId,
                PayloadSnapshot = payloadSnapshot,
                PaResponseSnapshot = paResponse,
                Detail = detail,
                CreatedUtc = _timeProvider.GetUtcNow(),
            },
            cancellationToken);
    }

    private sealed class LineJson
    {
        public string Date { get; set; } = string.Empty;

        public string Rate { get; set; } = string.Empty;

        public string Base { get; set; } = string.Empty;

        public string Vat { get; set; } = string.Empty;
    }
}
