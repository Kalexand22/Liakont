namespace Stratum.Modules.Notification.Contracts.DTOs;

public record ApiKeyCreatedDto
{
    public required Guid Id { get; init; }

    public required string FullKey { get; init; }
}
