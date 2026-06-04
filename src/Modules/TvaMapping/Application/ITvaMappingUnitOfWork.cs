namespace Liakont.Modules.TvaMapping.Application;

using Liakont.Modules.TvaMapping.Domain.Entities;

/// <summary>
/// Unité de travail transactionnelle du module TvaMapping. Toutes les écritures sont scopées par le
/// <c>company_id</c> porté par la <see cref="MappingTable"/> (CLAUDE.md n°9). Persistance de
/// paramétrage : aucune écriture sur une table d'audit (le journal append-only des modifications,
/// <c>MappingChangeLog</c>, est porté par l'édition TVA05).
/// </summary>
public interface ITvaMappingUnitOfWork : IAsyncDisposable
{
    /// <summary>
    /// Insère la table de mapping (en-tête + règles) du tenant de façon atomique. Lève une
    /// <see cref="Stratum.Common.Abstractions.Exceptions.ConflictException"/> si une table existe
    /// déjà pour ce tenant.
    /// </summary>
    Task InsertMappingTableAsync(MappingTable table, CancellationToken ct = default);

    /// <summary>Valide la transaction.</summary>
    Task CommitAsync(CancellationToken ct = default);
}

/// <summary>Fabrique d'unités de travail du module TvaMapping.</summary>
public interface ITvaMappingUnitOfWorkFactory
{
    /// <summary>Ouvre une nouvelle unité de travail (connexion + transaction).</summary>
    Task<ITvaMappingUnitOfWork> BeginAsync(CancellationToken ct = default);
}
