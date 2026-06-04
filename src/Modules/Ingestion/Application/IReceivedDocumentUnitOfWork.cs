namespace Liakont.Modules.Ingestion.Application;

using Liakont.Modules.Ingestion.Domain.Entities;
using Stratum.Common.Abstractions.Events;

/// <summary>
/// Unité de travail transactionnelle de la RÉCEPTION de documents (anti-doublon, métadonnées de
/// régimes source, publication d'événements), ouverte sur la base SYSTÈME (schéma <c>ingestion</c>),
/// F12 §3-4 / PIV04. Comme le registre d'agents, elle vit dans la base système et chaque écriture
/// porte son <c>tenant_id</c> ; toutes les lectures/écritures sont scopées au tenant de l'agent
/// authentifié (anti-fuite cross-tenant, CLAUDE.md n°9). Le registre de réception est append-only.
/// Les événements d'intégration (<c>DocumentReceived</c>, <c>SourceAlterationDetected</c>) sont
/// écrits dans l'outbox DANS LA MÊME TRANSACTION que l'insertion (cohérence transactionnelle).
/// </summary>
public interface IReceivedDocumentUnitOfWork : IAsyncDisposable
{
    /// <summary>Vrai si cette empreinte de payload est déjà connue pour ce tenant (doublon).</summary>
    Task<bool> PayloadHashExistsAsync(string tenantId, string payloadHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Empreinte du DERNIER payload reçu pour cette référence source (tenant), ou <c>null</c> si la
    /// référence n'a jamais été reçue — base de la détection d'altération.
    /// </summary>
    Task<string?> GetLatestHashForSourceReferenceAsync(string tenantId, string sourceReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Insère une entrée de réception. Lève <see cref="Stratum.Common.Abstractions.Exceptions.ConflictException"/>
    /// si l'empreinte (tenant + payload_hash) existe déjà (course entre lots concurrents) — l'appelant
    /// traite alors le document comme doublon.
    /// </summary>
    Task InsertReceivedDocumentAsync(ReceivedDocument receivedDocument, CancellationToken cancellationToken = default);

    /// <summary>Écrit un événement d'intégration dans l'outbox, dans la transaction courante (sans commit).</summary>
    Task WriteEventAsync<TPayload>(IntegrationEvent<TPayload> integrationEvent, CancellationToken cancellationToken = default);

    /// <summary>Valide la transaction courante.</summary>
    Task CommitAsync(CancellationToken cancellationToken = default);
}

/// <summary>Fabrique d'unités de travail de réception (ouvre une transaction sur la base système).</summary>
public interface IReceivedDocumentUnitOfWorkFactory
{
    Task<IReceivedDocumentUnitOfWork> BeginAsync(CancellationToken cancellationToken = default);
}
