namespace Liakont.Modules.Ged.Application.Ingestion;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Ged.Domain.Ingestion;
using Stratum.Common.Abstractions.Events;

/// <summary>
/// Unité de travail transactionnelle de la RÉCEPTION du canal GED (anti-doublon + publication d'événement), ouverte
/// sur la base SYSTÈME (schéma <c>ged_ingestion</c>), F19 §4.3.1 / GED05b. Miroir EXACT de
/// <c>IReceivedDocumentUnitOfWork</c> (canal fiscal), mais espace de hash STRICTEMENT SÉPARÉ (RL-01) : le canal GED
/// ne référence JAMAIS <c>Ingestion.Application</c>. Chaque écriture porte son <c>tenant_id</c> ; toute lecture est
/// scopée au tenant de l'agent authentifié (anti-fuite cross-tenant, CLAUDE.md n°9). Le registre est append-only.
/// L'événement <c>ManagedDocumentReceivedV1</c> est écrit dans l'outbox DANS LA MÊME TRANSACTION que l'INSERT du
/// registre (atomicité registre + événement, RL-03 : il n'existe pas de 2PC entre deux bases PG).
/// </summary>
public interface IGedReceivedDocumentUnitOfWork : IAsyncDisposable
{
    /// <summary>Vrai si cette empreinte de payload est déjà connue pour ce tenant (doublon GED).</summary>
    Task<bool> PayloadHashExistsAsync(string tenantId, string payloadHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Empreinte du DERNIER payload reçu pour cette référence source (tenant), ou <see langword="null"/> si la
    /// référence n'a jamais été reçue — base de la détection d'altération.
    /// </summary>
    Task<string?> GetLatestHashForSourceReferenceAsync(string tenantId, string sourceReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Insère une entrée de registre GED. Lève <see cref="Stratum.Common.Abstractions.Exceptions.ConflictException"/>
    /// si l'empreinte (tenant + payload_hash) existe déjà (course entre lots concurrents) — l'appelant traite alors
    /// le document comme doublon.
    /// </summary>
    Task InsertReceivedDocumentAsync(GedReceivedDocument receivedDocument, CancellationToken cancellationToken = default);

    /// <summary>Écrit un événement d'intégration dans l'outbox, dans la transaction courante (sans commit).</summary>
    Task WriteEventAsync<TPayload>(IntegrationEvent<TPayload> integrationEvent, CancellationToken cancellationToken = default);

    /// <summary>Valide la transaction courante (registre + événement deviennent durables atomiquement).</summary>
    Task CommitAsync(CancellationToken cancellationToken = default);
}

/// <summary>Fabrique d'unités de travail de réception GED (ouvre une transaction sur la base système).</summary>
public interface IGedReceivedDocumentUnitOfWorkFactory
{
    /// <summary>Ouvre une transaction fraîche sur la base système (schéma <c>ged_ingestion</c> + outbox).</summary>
    Task<IGedReceivedDocumentUnitOfWork> BeginAsync(CancellationToken cancellationToken = default);
}
