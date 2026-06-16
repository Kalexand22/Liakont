namespace Liakont.Modules.Mandats.Application;

using Liakont.Modules.Mandats.Domain.Entities;

/// <summary>
/// Unité de travail transactionnelle du module Mandats. Toutes les écritures sont scopées par le
/// <c>company_id</c> porté par l'agrégat (CLAUDE.md n°9, INV-MANDATS-1). Chaque mutation (registre ou
/// cycle de vie) persiste l'agrégat ET son entrée de journal append-only dans la MÊME transaction
/// (atomicité — « pas de mutation sans ligne de journal », ADR-0022 §3 / INV-MANDATS-3).
/// </summary>
public interface IMandatsUnitOfWork : IAsyncDisposable
{
    /// <summary>
    /// Insère un mandant (registre) ET son entrée de journal (<c>CreateMandant</c>) dans la même
    /// transaction. Lève une <c>ConflictException</c> (Stratum.Common) si un mandant de même
    /// (<c>company_id</c>, <c>reference</c>) existe déjà.
    /// </summary>
    Task InsertMandantAsync(Mandant mandant, MandatChangeLogEntry changeLogEntry, CancellationToken ct = default);

    /// <summary>
    /// Charge un mandant pour édition, en VERROUILLANT sa ligne (<c>FOR UPDATE</c>) dans la transaction
    /// courante : deux éditions concurrentes sont sérialisées. <c>null</c> si absent pour ce tenant.
    /// </summary>
    Task<Mandant?> GetMandantForUpdateAsync(Guid companyId, string reference, CancellationToken ct = default);

    /// <summary>Persiste ATOMIQUEMENT une mutation de mandant et son entrée de journal (mise à jour + journal).</summary>
    Task SaveMandantMutationAsync(Mandant mandant, MandatChangeLogEntry changeLogEntry, CancellationToken ct = default);

    /// <summary>
    /// Insère un mandat (cycle de vie) ET son entrée de journal (<c>CreateMandat</c>) dans la même
    /// transaction. Lève une <c>ConflictException</c> (Stratum.Common) si un mandat de même
    /// (<c>company_id</c>, <c>mandant_id</c>, <c>reference</c>) existe déjà.
    /// </summary>
    Task InsertMandatAsync(Mandat mandat, MandatChangeLogEntry changeLogEntry, CancellationToken ct = default);

    /// <summary>Charge un mandat pour édition, en VERROUILLANT sa ligne (<c>FOR UPDATE</c>). <c>null</c> si absent pour ce tenant.</summary>
    Task<Mandat?> GetMandatForUpdateAsync(Guid companyId, Guid mandantId, string reference, CancellationToken ct = default);

    /// <summary>
    /// Persiste ATOMIQUEMENT une mutation de mandat et son entrée de journal : met à jour le mandat (clause,
    /// statut, validation, révocation) et insère <paramref name="changeLogEntry"/> dans le journal
    /// append-only — le tout dans la même transaction (un échec avant <see cref="CommitAsync"/> ne laisse rien).
    /// </summary>
    Task SaveMandatMutationAsync(Mandat mandat, MandatChangeLogEntry changeLogEntry, CancellationToken ct = default);

    /// <summary>Valide la transaction.</summary>
    Task CommitAsync(CancellationToken ct = default);
}

/// <summary>Fabrique d'unités de travail du module Mandats.</summary>
public interface IMandatsUnitOfWorkFactory
{
    /// <summary>Ouvre une nouvelle unité de travail (connexion + transaction).</summary>
    Task<IMandatsUnitOfWork> BeginAsync(CancellationToken ct = default);
}
