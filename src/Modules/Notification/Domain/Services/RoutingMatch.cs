namespace Stratum.Modules.Notification.Domain.Services;

using Stratum.Modules.Notification.Domain.Entities;

public record RoutingMatch(
    string RuleCode,
    string RuleName,
    string ServiceCode,
    RecipientType RecipientType,
    string RecipientValue,
    int Priority);
