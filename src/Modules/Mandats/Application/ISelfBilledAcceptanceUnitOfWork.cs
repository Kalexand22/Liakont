namespace Liakont.Modules.Mandats.Application;

using Liakont.Modules.Mandats.Domain.Entities;

/// <summary>
/// Unité de travail transactionnelle de l'agrégat <see cref="SelfBilledAcceptance"/> (ADR-0024). Toutes les
/// écritures sont scopées par le <c>company_id</c> porté par l'agrégat (CLAUDE.md n°9, INV-MANDATS-1). CHAQUE
/// transition d'état (création incluse) persiste l'agrégat ET son entrée de journal append-only
/// (<c>self_billed_acceptance_log</c>) dans la MÊME transaction (atomicité — « pas de transition sans ligne
/// de journal », ADR-0024 §6 / INV-ACCEPT-5).
/// </summary>
public interface ISelfBilledAcceptanceUnitOfWork : IAsyncDisposable
{
    /// <summary>
    /// Insère une acceptation (état initial <see cref="SelfBilledAcceptanceState.PendingAcceptance"/>) ET son
    /// entrée de journal de genèse dans la même transaction. Lève une <c>ConflictException</c> (Stratum.Common)
    /// si une acceptation existe déjà pour ce <c>(company_id, document_id)</c>.
    /// </summary>
    Task InsertAsync(SelfBilledAcceptance acceptance, SelfBilledAcceptanceLogEntry logEntry, CancellationToken ct = default);

    /// <summary>
    /// Charge une acceptation pour transition, en VERROUILLANT sa ligne (<c>FOR UPDATE</c>) dans la transaction
    /// courante : deux transitions concurrentes sont sérialisées. <c>null</c> si absente pour ce tenant.
    /// </summary>
    Task<SelfBilledAcceptance?> GetForUpdateAsync(Guid companyId, Guid documentId, CancellationToken ct = default);

    /// <summary>
    /// Persiste ATOMIQUEMENT une transition d'état et son entrée de journal : met à jour l'acceptation et insère
    /// <paramref name="logEntry"/> dans le journal append-only — le tout dans la même transaction (un échec avant
    /// <see cref="CommitAsync"/> ne laisse rien).
    /// </summary>
    Task SaveTransitionAsync(SelfBilledAcceptance acceptance, SelfBilledAcceptanceLogEntry logEntry, CancellationToken ct = default);

    /// <summary>Valide la transaction.</summary>
    Task CommitAsync(CancellationToken ct = default);
}

/// <summary>Fabrique d'unités de travail de l'agrégat <see cref="SelfBilledAcceptance"/>.</summary>
public interface ISelfBilledAcceptanceUnitOfWorkFactory
{
    /// <summary>Ouvre une nouvelle unité de travail (connexion + transaction).</summary>
    Task<ISelfBilledAcceptanceUnitOfWork> BeginAsync(CancellationToken ct = default);
}
