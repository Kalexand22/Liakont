namespace Stratum.Modules.Identity.Contracts.Events;

public record UserCreatedV1
{
    public required Guid UserId { get; init; }

    public required string Username { get; init; }

    public required string Email { get; init; }

    public required string DisplayName { get; init; }

    public Guid? PartyId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
