namespace Liakont.Host.Compatibility;

using Stratum.Common.Abstractions.Queries;
using Stratum.Modules.Party.Contracts.DTOs;
using Stratum.Modules.Party.Contracts.Queries;

/// <summary>
/// Implémentation no-op de <see cref="IPartyQueries"/> pour Liakont.
///
/// Le module ERP Party n'est PAS vendoré (seul <c>Party.Contracts</c> l'est — décision D1 du
/// 2026-06-03) ; or <c>Identity.Infrastructure</c> (CreateUserHandler) dépend de
/// <see cref="IPartyQueries"/> par injection. Liakont ne lie pas ses utilisateurs à des « Party »
/// ERP : le <c>PartyId</c> d'un utilisateur est toujours <c>null</c>, donc ces requêtes ne sont
/// jamais réellement invoquées. Ce shim satisfait la validation du graphe DI
/// (<c>ValidateOnBuild</c>, activé en Development) sans tirer <c>Party.Infrastructure</c> (qui
/// dépend de modules non vendorés). Consigné dans <c>provenance-socle-stratum.md</c> §4.10.
/// </summary>
internal sealed class NullPartyQueries : IPartyQueries
{
    public Task<PartyDto?> GetById(Guid partyId, CancellationToken ct = default)
        => Task.FromResult<PartyDto?>(null);

    public Task<PartyWithRolesDto?> GetByIdWithRoles(Guid partyId, CancellationToken ct = default)
        => Task.FromResult<PartyWithRolesDto?>(null);

    public Task<IReadOnlyList<PartyDto>> Search(
        string? nameTerm,
        string? roleCode,
        bool? isActive,
        int limit = 50,
        int offset = 0,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PartyDto>>([]);

    public Task<ListResult<PartyDto>> SearchPaged(ListQuery query, CancellationToken ct = default)
        => Task.FromResult(new ListResult<PartyDto>());

    public Task<IReadOnlyList<AddressDto>> GetAddresses(Guid partyId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AddressDto>>([]);

    public Task<IReadOnlyList<ContactDto>> GetContacts(Guid partyId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ContactDto>>([]);

    public Task<bool> HasRole(Guid partyId, string roleCode, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<IReadOnlyList<string>> GetExistingTaxIds(
        IReadOnlyList<string> taxIds,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([]);
}
