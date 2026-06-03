namespace Stratum.Modules.Party.Contracts.DTOs;

public record ExternalIdDto
{
    public required Guid Id { get; init; }

    public required Guid PartyId { get; init; }

    public required string SystemCode { get; init; }

    public required string ExternalId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
