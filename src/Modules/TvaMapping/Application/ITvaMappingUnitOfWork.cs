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

    /// <summary>
    /// Insère la table de mapping (en-tête + règles) ET ajoute, dans la MÊME transaction, les entrées de
    /// journal append-only décrivant sa naissance (item FIX01b : « Créer la table » sur l'état vide, ou
    /// création implicite à la première règle — entrées <c>CreateTable</c> puis éventuellement
    /// <c>AddRule</c>). Atomicité : un échec avant <see cref="CommitAsync"/> ne laisse rien persisté (ni
    /// table, ni journal). Lève une
    /// <see cref="Stratum.Common.Abstractions.Exceptions.ConflictException"/> si une table existe déjà
    /// pour ce tenant.
    /// </summary>
    Task InsertMappingTableAsync(
        MappingTable table,
        IReadOnlyList<MappingChangeLogEntry> changeLog,
        CancellationToken ct = default);

    /// <summary>
    /// Charge la table du tenant pour édition, en VERROUILLANT sa ligne d'en-tête (<c>FOR UPDATE</c>)
    /// dans la transaction courante : deux éditions concurrentes sont sérialisées. Re-valide la
    /// structure au chargement (item TVA01 §4). Retourne <c>null</c> si aucune table n'est paramétrée.
    /// </summary>
    Task<MappingTable?> GetForUpdateAsync(Guid companyId, CancellationToken ct = default);

    /// <summary>
    /// Persiste de façon ATOMIQUE (item TVA05 §5) une mutation de table et son entrée de journal : met
    /// à jour l'en-tête (état de validation, date de modification), réécrit les règles, et insère
    /// <paramref name="changeLogEntry"/> dans le journal append-only — le tout dans la même transaction.
    /// La transaction n'est validée que par <see cref="CommitAsync"/> : un échec avant le commit ne
    /// laisse rien persisté (ni mutation, ni journal).
    /// </summary>
    Task SaveMutationAsync(MappingTable table, MappingChangeLogEntry changeLogEntry, CancellationToken ct = default);

    /// <summary>Valide la transaction.</summary>
    Task CommitAsync(CancellationToken ct = default);
}

/// <summary>Fabrique d'unités de travail du module TvaMapping.</summary>
public interface ITvaMappingUnitOfWorkFactory
{
    /// <summary>Ouvre une nouvelle unité de travail (connexion + transaction).</summary>
    Task<ITvaMappingUnitOfWork> BeginAsync(CancellationToken ct = default);
}
