namespace Stratum.Modules.Identity.Contracts.DTOs;

public record DelegationDto
{
    public required Guid Id { get; init; }

    public required Guid DelegatorId { get; init; }

    public required string DelegatorName { get; init; }

    public required Guid DelegateId { get; init; }

    public required string DelegateName { get; init; }

    public required string Scope { get; init; }

    public required DateTimeOffset ValidFrom { get; init; }

    public required DateTimeOffset ValidUntil { get; init; }

    public string? Reason { get; init; }

    public bool IsActive { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
