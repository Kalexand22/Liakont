namespace Stratum.Modules.Identity.Contracts.DTOs;

public record GrantConditionDto
{
    public required Guid GrantId { get; init; }

    public required string Permission { get; init; }

    public string? Condition { get; init; }
}
