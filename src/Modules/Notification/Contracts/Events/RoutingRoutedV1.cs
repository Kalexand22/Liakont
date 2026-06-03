namespace Stratum.Modules.Notification.Contracts.Events;

public record RoutingRoutedV1
{
    public required string EntityType { get; init; }

    public required string EntityId { get; init; }

    public required IReadOnlyList<string> MatchedServices { get; init; }

    public required string TemplateCode { get; init; }

    public Guid? CompanyId { get; init; }

    public required DateTimeOffset RoutedAt { get; init; }
}
