namespace Stratum.Modules.Notification.Infrastructure.Handlers.Commands;

using System.Text.Json;
using MediatR;
using Stratum.Modules.Notification.Application;
using Stratum.Modules.Notification.Contracts.Commands;
using Stratum.Modules.Notification.Domain.Entities;
using Stratum.Modules.Notification.Domain.ValueObjects;

public sealed class CreateRoutingRuleHandler : IRequestHandler<CreateRoutingRuleCommand, Guid>
{
    private readonly INotificationUnitOfWorkFactory _uowFactory;

    public CreateRoutingRuleHandler(INotificationUnitOfWorkFactory uowFactory)
    {
        _uowFactory = uowFactory;
    }

    public async Task<Guid> Handle(CreateRoutingRuleCommand request, CancellationToken cancellationToken)
    {
        var recipientType = Enum.Parse<RecipientType>(request.RecipientType, ignoreCase: true);
        var conditions = ParseConditions(request.ConditionsJson);

        var rule = RoutingRule.Create(
            request.Code,
            request.Name,
            request.EntityType,
            request.ServiceCode,
            recipientType,
            request.RecipientValue,
            conditions,
            request.Priority,
            request.CompanyId);

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);

        await uow.InsertRoutingRuleAsync(rule, cancellationToken);
        await uow.CommitAsync(cancellationToken);

        return rule.Id;
    }

    private static List<RoutingCondition> ParseConditions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        var elements = JsonSerializer.Deserialize<JsonElement[]>(json) ?? [];
        return elements.Select(ParseCondition).ToList();
    }

    private static RoutingCondition ParseCondition(JsonElement element)
    {
        if (element.TryGetProperty("and", out var andChildren))
        {
            var children = andChildren.EnumerateArray().Select(ParseCondition).ToList();
            return RoutingCondition.Compound("and", children);
        }

        if (element.TryGetProperty("or", out var orChildren))
        {
            var children = orChildren.EnumerateArray().Select(ParseCondition).ToList();
            return RoutingCondition.Compound("or", children);
        }

        var field = element.GetProperty("field").GetString()!;
        var op = element.GetProperty("op").GetString()!;
        JsonElement? value = element.TryGetProperty("value", out var v) ? v : null;
        return RoutingCondition.Leaf(field, op, value);
    }
}
