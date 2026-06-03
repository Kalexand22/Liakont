namespace Stratum.Modules.Notification.Infrastructure.Services;

using System.Text.Json;
using Stratum.Modules.Notification.Application;
using Stratum.Modules.Notification.Contracts;
using Stratum.Modules.Notification.Contracts.DTOs;
using Stratum.Modules.Notification.Domain.Services;

internal sealed class RoutingEngine : IRoutingEngine
{
    private readonly INotificationUnitOfWorkFactory _uowFactory;

    public RoutingEngine(INotificationUnitOfWorkFactory uowFactory)
    {
        _uowFactory = uowFactory;
    }

    public async Task<IReadOnlyList<RoutingResultDto>> EvaluateRoutingAsync(
        string entityType,
        IReadOnlyDictionary<string, JsonElement> data,
        Guid? companyId,
        CancellationToken ct)
    {
        await using var uow = await _uowFactory.BeginAsync(ct);

        var rules = await uow.GetActiveRoutingRulesAsync(entityType, companyId, ct);
        var matches = RoutingEvaluator.Evaluate(rules, data);

        return matches.Select(m => new RoutingResultDto
        {
            RuleCode = m.RuleCode,
            RuleName = m.RuleName,
            ServiceCode = m.ServiceCode,
            RecipientType = m.RecipientType.ToString(),
            RecipientValue = m.RecipientValue,
            Priority = m.Priority,
        }).ToList();
    }
}
