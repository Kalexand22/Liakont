namespace Liakont.Modules.Documents.Infrastructure;

using Liakont.Modules.Documents.Domain.Entities;
using Liakont.Modules.Ingestion.Contracts.Events;
using Microsoft.Extensions.Logging;
using Npgsql;
using Stratum.Common.Abstractions.Events;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Consomme l'événement d'intégration <see cref="SourceAlterationDetectedV1"/> publié par l'ingestion
/// (PIV04) quand une <c>source_reference</c> déjà connue est re-poussée avec une empreinte de payload
/// DIFFÉRENTE (item TRK03, F06 §3 « double usage du payload_hash »). Si la plateforme a DÉJÀ ÉMIS un
/// document pour cette référence, on inscrit un fait d'audit append-only
/// (<see cref="DocumentEventType.DocumentSourceAlteredAfterIssue"/>) SUR ce document émis. Le document
/// émis n'est JAMAIS réémis ni mis à jour (DR6 point 2 : toute altération laisse une trace, jamais de
/// réémission silencieuse). Si aucun document n'est émis pour la référence, il n'y a rien à signaler sur
/// l'émis : le nouveau document suit le circuit normal (un <see cref="DocumentReceivedV1"/> est publié en
/// parallèle par l'ingestion).
/// </summary>
/// <remarks>
/// Le worker d'outbox dispatche les consommateurs dans un scope SYSTÈME (aucun contexte tenant établi) ;
/// le tenant est donc résolu par le SLUG porté par l'événement (<see cref="SourceAlterationDetectedV1.TenantId"/>)
/// via <see cref="ITenantConnectionFactory"/> — même mécanique que le port d'ingestion <c>DocumentIntake</c>.
/// La consommation est IDEMPOTENTE : l'identifiant de l'entrée d'audit est celui de l'événement
/// d'intégration ; un rejeu (livraison at-least-once) heurte la clé primaire et n'inscrit jamais deux fois
/// la même altération.
/// </remarks>
public sealed partial class SourceAlterationDetectedConsumer : IIntegrationEventConsumer<SourceAlterationDetectedV1>
{
    private readonly ITenantConnectionFactory _tenantConnectionFactory;
    private readonly ILogger<SourceAlterationDetectedConsumer> _logger;

    public SourceAlterationDetectedConsumer(
        ITenantConnectionFactory tenantConnectionFactory,
        ILogger<SourceAlterationDetectedConsumer> logger)
    {
        _tenantConnectionFactory = tenantConnectionFactory;
        _logger = logger;
    }

    public async Task HandleAsync(IntegrationEvent<SourceAlterationDetectedV1> integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var payload = integrationEvent.Payload;

        var connectionFactory = new TenantSlugConnectionFactoryAdapter(_tenantConnectionFactory, payload.TenantId);
        await using var uow = await PostgresDocumentUnitOfWork.BeginAsync(connectionFactory, cancellationToken);

        var issued = await uow.FindMostRecentIssuedBySourceReferenceAsync(payload.SourceReference, cancellationToken);
        if (issued is null)
        {
            // Altération d'une source SANS document émis : rien à signaler sur l'émis (le nouveau document
            // suit le circuit normal). On ne crée aucun faux signal d'intégrité.
            LogNoIssuedDocument(_logger, payload.SourceReference);
            return;
        }

        var (issuedId, issuedNumber) = issued.Value;

        var detail =
            $"Altération de la source détectée APRÈS émission : le document source « {payload.SourceReference} » " +
            $"(document émis n° {issuedNumber}) a été re-soumis avec une empreinte différente " +
            $"(précédente {payload.PreviousPayloadHash}, nouvelle {payload.NewPayloadHash}) sous le nouveau " +
            $"document {payload.DocumentId}. Le document émis n'est NI modifié NI réémis (piste d'audit, F06 §3). " +
            "Action : vérifier la cohérence entre la donnée source et le document déjà transmis à la Plateforme Agréée.";

        var auditEvent = DocumentEvent.SourceAlteredAfterIssue(
            integrationEvent.EventId,
            issuedId,
            payload.DetectedAtUtc,
            detail);

        try
        {
            await uow.AppendEventAsync(auditEvent, cancellationToken);
            await uow.CommitAsync(cancellationToken);
            LogAlterationRecorded(_logger, issuedNumber, payload.SourceReference);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Rejeu de l'événement d'outbox (livraison at-least-once) : la même altération a déjà été
            // inscrite (clé primaire = identifiant de l'événement d'intégration) — no-op idempotent.
            LogAlterationAlreadyRecorded(_logger, integrationEvent.EventId);
        }
    }

    [LoggerMessage(EventId = 6100, Level = LogLevel.Warning,
        Message = "Altération source après émission inscrite sur le document émis n° {DocumentNumber} (source {SourceReference}).")]
    private static partial void LogAlterationRecorded(ILogger logger, string documentNumber, string sourceReference);

    [LoggerMessage(EventId = 6101, Level = LogLevel.Debug,
        Message = "Altération source signalée pour la référence {SourceReference} sans document émis : aucun fait d'audit à inscrire.")]
    private static partial void LogNoIssuedDocument(ILogger logger, string sourceReference);

    [LoggerMessage(EventId = 6102, Level = LogLevel.Debug,
        Message = "Altération source déjà inscrite pour l'événement d'intégration {EventId} (rejeu idempotent).")]
    private static partial void LogAlterationAlreadyRecorded(ILogger logger, Guid eventId);
}
