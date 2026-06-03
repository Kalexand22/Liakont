namespace Stratum.Modules.Party.Contracts.DTOs;

public record ContactDto
{
    public required Guid Id { get; init; }

    public required Guid PartyId { get; init; }

    public required string ContactType { get; init; }

    public string? Label { get; init; }

    public required string Value { get; init; }

    public required bool IsPrimary { get; init; }
}
