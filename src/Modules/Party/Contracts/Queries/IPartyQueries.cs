namespace Stratum.Modules.Party.Contracts.Queries;

using Stratum.Common.Abstractions.Queries;
using Stratum.Modules.Party.Contracts.DTOs;

public interface IPartyQueries
{
    Task<PartyDto?> GetById(Guid partyId, CancellationToken ct = default);

    Task<PartyWithRolesDto?> GetByIdWithRoles(Guid partyId, CancellationToken ct = default);

    Task<IReadOnlyList<PartyDto>> Search(
        string? nameTerm,
        string? roleCode,
        bool? isActive,
        int limit = 50,
        int offset = 0,
        CancellationToken ct = default);

    Task<ListResult<PartyDto>> SearchPaged(ListQuery query, CancellationToken ct = default);

    Task<IReadOnlyList<AddressDto>> GetAddresses(Guid partyId, CancellationToken ct = default);

    Task<IReadOnlyList<ContactDto>> GetContacts(Guid partyId, CancellationToken ct = default);

    Task<bool> HasRole(Guid partyId, string roleCode, CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetExistingTaxIds(IReadOnlyList<string> taxIds, CancellationToken ct = default);
}
