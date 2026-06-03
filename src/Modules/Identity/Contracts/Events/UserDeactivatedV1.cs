namespace Stratum.Modules.Identity.Contracts.Events;

public record UserDeactivatedV1
{
    public required Guid UserId { get; init; }

    public required DateTimeOffset DeactivatedAt { get; init; }
}
