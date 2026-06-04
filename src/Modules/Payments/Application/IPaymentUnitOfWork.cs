namespace Liakont.Modules.Payments.Application;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Payments.Domain.Entities;

/// <summary>
/// Unité de travail d'écriture du module Payments (item TRK04). Ouverte sur la base DU TENANT (la connexion
/// EST le tenant — database-per-tenant, blueprint §7) : aucune opération n'est cross-tenant. L'agrégat et sa
/// piste d'audit (<see cref="PaymentAggregateEvent"/>) sont écrits ATOMIQUEMENT dans la transaction de
/// l'unité de travail. Interne au module (consommée par le pipeline aval — PIP03).
/// </summary>
public interface IPaymentUnitOfWork : IAsyncDisposable
{
    /// <summary>
    /// Persiste un encaissement brut de façon IDEMPOTENTE sur l'identifiant : si un paiement de même
    /// identifiant existe déjà (re-push), rien n'est inséré et la méthode retourne <c>false</c>. Retourne
    /// <c>true</c> si le paiement a été créé.
    /// </summary>
    Task<bool> SavePaymentAsync(Payment payment, CancellationToken cancellationToken = default);

    /// <summary>
    /// Crée l'agrégat en état <c>Calculated</c> de façon IDEMPOTENTE sur l'identifiant, en écrivant son
    /// événement de genèse dans la MÊME transaction. Si l'agrégat existe déjà, rien n'est inséré (ni agrégat,
    /// ni événement) et la méthode retourne <c>false</c>.
    /// </summary>
    Task<bool> CreateAggregateAsync(PaymentAggregate aggregate, PaymentAggregateEvent genesisEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Charge l'agrégat DANS la transaction courante AVEC un verrou de ligne (<c>SELECT … FOR UPDATE</c>),
    /// pour un read-modify-write sûr (transition de transmission + événement d'audit sans qu'une transition
    /// concurrente ne s'intercale). Retourne <c>null</c> si l'agrégat n'existe pas dans ce tenant.
    /// </summary>
    Task<PaymentAggregate?> GetAggregateForUpdateAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Insère ou met à jour un agrégat par identifiant (primitive utilisée par le pipeline : état de
    /// transmission). N'écrit PAS d'événement d'audit — c'est <see cref="AppendAggregateEventAsync"/> qui le
    /// fait, dans la même transaction.
    /// </summary>
    Task UpsertAggregateAsync(PaymentAggregate aggregate, CancellationToken cancellationToken = default);

    /// <summary>Ajoute une entrée à la piste d'audit des agrégats (append-only — aucun update/delete possible, garanti en base).</summary>
    Task AppendAggregateEventAsync(PaymentAggregateEvent aggregateEvent, CancellationToken cancellationToken = default);

    /// <summary>Valide la transaction.</summary>
    Task CommitAsync(CancellationToken cancellationToken = default);
}

/// <summary>Fabrique d'unités de travail Payments pour le tenant COURANT (résolu par la connexion scopée).</summary>
public interface IPaymentUnitOfWorkFactory
{
    Task<IPaymentUnitOfWork> BeginAsync(CancellationToken cancellationToken = default);
}
