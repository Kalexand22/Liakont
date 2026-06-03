namespace Stratum.Modules.Identity.Tests.Integration;

using Stratum.Common.Abstractions.Queries;
using Stratum.Modules.Party.Contracts.DTOs;
using Stratum.Modules.Party.Contracts.Queries;

/// <summary>Stub IPartyQueries — returns null for all queries (no Party dependency in auth flow tests).</summary>
internal sealed class NullPartyQueries : IPartyQueries
{
    public Task<PartyDto?> GetById(Guid partyId, CancellationToken ct = default)
        => Task.FromResult<PartyDto?>(null);

    public Task<PartyWithRolesDto?> GetByIdWithRoles(Guid partyId, CancellationToken ct = default)
        => Task.FromResult<PartyWithRolesDto?>(null);

    public Task<IReadOnlyList<PartyDto>> Search(string? nameTerm, string? roleCode, bool? isActive, int limit = 50, int offset = 0, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PartyDto>>(Array.Empty<PartyDto>());

    public Task<ListResult<PartyDto>> SearchPaged(ListQuery query, CancellationToken ct = default)
        => Task.FromResult(new ListResult<PartyDto> { Items = [], TotalCount = 0 });

    public Task<IReadOnlyList<AddressDto>> GetAddresses(Guid partyId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AddressDto>>(Array.Empty<AddressDto>());

    public Task<IReadOnlyList<ContactDto>> GetContacts(Guid partyId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ContactDto>>(Array.Empty<ContactDto>());

    public Task<bool> HasRole(Guid partyId, string roleCode, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<IReadOnlyList<string>> GetExistingTaxIds(IReadOnlyList<string> taxIds, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
}
