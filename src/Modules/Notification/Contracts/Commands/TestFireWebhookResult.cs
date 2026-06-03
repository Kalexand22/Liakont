namespace Stratum.Modules.Notification.Contracts.Commands;

public record TestFireWebhookResult
{
    public required bool Success { get; init; }

    public required int StatusCode { get; init; }

    public string? ErrorMessage { get; init; }
}
