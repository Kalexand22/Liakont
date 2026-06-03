namespace Stratum.Modules.Party.Contracts.DTOs;

public record PartyWithRolesDto
{
    public required Guid Id { get; init; }

    public required string LegalName { get; init; }

    public string? TradeName { get; init; }

    public required string PartyType { get; init; }

    public string? TaxId { get; init; }

    public required bool IsActive { get; init; }

    public required IReadOnlyList<PartyRoleDto> Roles { get; init; }
}
