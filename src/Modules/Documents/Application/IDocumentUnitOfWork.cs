namespace Liakont.Modules.Documents.Application;

using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Documents.Domain.Entities;

/// <summary>
/// Unité de travail d'écriture du module Documents (item TRK01). Ouverte sur la base DU TENANT (la
/// connexion EST le tenant — database-per-tenant, blueprint §7) : aucune opération n'est cross-tenant.
/// Les écritures du document et de sa piste d'audit (<see cref="DocumentEvent"/>) sont ATOMIQUES dans
/// la transaction de l'unité de travail. Interne au module (consommée par l'ingestion via le port
/// <c>IDocumentIntake</c> et par le pipeline aval).
/// </summary>
public interface IDocumentUnitOfWork : System.IAsyncDisposable
{
    /// <summary>
    /// Crée le document en état <c>Detected</c> de façon IDEMPOTENTE sur l'identifiant (F06 / contrat
    /// <c>IDocumentIntake</c>) : si un document de même identifiant existe déjà, rien n'est inséré (ni le
    /// document, ni l'événement de genèse) et la méthode retourne <c>false</c> — un re-push d'ingestion
    /// ne duplique jamais le document ni n'écrase un état déjà avancé. Retourne <c>true</c> si le
    /// document (et son événement de genèse) ont été créés.
    /// </summary>
    Task<bool> CreateDetectedAsync(Document document, DocumentEvent genesisEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Insère ou met à jour un document par identifiant (primitive de repository utilisée par le
    /// pipeline aval : transitions d'état, identifiants PA, version de mapping). N'écrit PAS d'événement
    /// d'audit — c'est <see cref="AppendEventAsync"/> qui le fait, dans la même transaction.
    /// </summary>
    Task UpsertDocumentAsync(Document document, CancellationToken cancellationToken = default);

    /// <summary>Ajoute une entrée à la piste d'audit (append-only — aucun update/delete possible, garanti en base).</summary>
    Task AppendEventAsync(DocumentEvent documentEvent, CancellationToken cancellationToken = default);

    /// <summary>Valide la transaction.</summary>
    Task CommitAsync(CancellationToken cancellationToken = default);
}

/// <summary>Fabrique d'unités de travail Documents pour le tenant COURANT (résolu par la connexion scopée).</summary>
public interface IDocumentUnitOfWorkFactory
{
    Task<IDocumentUnitOfWork> BeginAsync(CancellationToken cancellationToken = default);
}
