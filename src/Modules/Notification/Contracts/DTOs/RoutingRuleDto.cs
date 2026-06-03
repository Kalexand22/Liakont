namespace Stratum.Modules.Notification.Contracts.DTOs;

public record RoutingRuleDto
{
    public required Guid Id { get; init; }

    public required string Code { get; init; }

    public required string Name { get; init; }

    public required string EntityType { get; init; }

    public required string ServiceCode { get; init; }

    public required string RecipientType { get; init; }

    public required string RecipientValue { get; init; }

    public required string ConditionsJson { get; init; }

    public required int Priority { get; init; }

    public required bool IsActive { get; init; }

    public Guid? CompanyId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}
