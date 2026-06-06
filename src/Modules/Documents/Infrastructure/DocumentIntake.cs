namespace Liakont.Modules.Documents.Infrastructure;

using System;
using Dapper;
using Liakont.Modules.Documents.Domain.Entities;
using Liakont.Modules.Ingestion.Contracts;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Implémentation du port <see cref="IDocumentIntake"/> (contrat PIV04) par le module Documents
/// (item TRK01) : crée le document en état <c>Detected</c> avec son événement d'audit de genèse, dans
/// la base DU TENANT. Remplace le shim <c>NoOpDocumentIntake</c> (enregistré comme défaut sûr par le
/// module Ingestion) au niveau de la composition.
/// </summary>
/// <remarks>
/// IDEMPOTENT sur <see cref="DetectedDocumentIntake.DocumentId"/> (contrat de cohérence
/// d'<see cref="IDocumentIntake"/>) : un re-push ou un rejeu de l'événement <c>DocumentReceived</c> ne
/// duplique jamais le document. Ce port est le FAST-PATH synchrone appelé en best-effort après le commit
/// de la réception ; le déclencheur DURABLE du pipeline reste l'événement outbox.
/// <para>
/// Le tenant est résolu par le SLUG transmis (<see cref="DetectedDocumentIntake.TenantId"/>) via
/// <see cref="ITenantConnectionFactory"/>, et non par <c>ITenantContext</c> : l'ingestion s'exécute sur
/// un endpoint de niveau système (résolution clé API -> tenant avant tout contexte tenant, F12 §3.1).
/// </para>
/// </remarks>
internal sealed class DocumentIntake : IDocumentIntake
{
    private readonly ITenantConnectionFactory _tenantConnectionFactory;

    public DocumentIntake(ITenantConnectionFactory tenantConnectionFactory)
    {
        _tenantConnectionFactory = tenantConnectionFactory;
    }

    public async Task RegisterDetectedDocumentAsync(DetectedDocumentIntake input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var document = DetectedDocumentMapper.ToDetectedDocument(input);
        var genesisEvent = DocumentEvent.Detected(document.Id, input.ReceivedAtUtc);

        var connectionFactory = new TenantSlugConnectionFactoryAdapter(_tenantConnectionFactory, input.TenantId);
        await using var uow = await PostgresDocumentUnitOfWork.BeginAsync(connectionFactory, cancellationToken);

        await uow.CreateDetectedAsync(document, genesisEvent, cancellationToken);
        await uow.CommitAsync(cancellationToken);
    }

    public async Task<bool> IsDocumentRangedAsync(Guid documentId, string tenantId, CancellationToken cancellationToken = default)
    {
        // Le document est « rangé » dès qu'il existe (Detected et au-delà) dans la base DU TENANT : lecture seule,
        // tenant-scopée par le slug (l'ingestion est de niveau système, F12 §3.1) — même résolution que la création.
        var connectionFactory = new TenantSlugConnectionFactoryAdapter(_tenantConnectionFactory, tenantId);
        using var connection = await connectionFactory.OpenAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS(SELECT 1 FROM documents.documents WHERE id = @Id)",
            new { Id = documentId },
            cancellationToken: cancellationToken));
    }
}
