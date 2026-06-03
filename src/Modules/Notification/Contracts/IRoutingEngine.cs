namespace Stratum.Modules.Notification.Contracts;

using Stratum.Modules.Notification.Contracts.DTOs;

/// <summary>
/// Evaluates routing rules for a given entity type and data.
/// Returns matched services/recipients ordered by priority.
/// </summary>
public interface IRoutingEngine
{
    Task<IReadOnlyList<RoutingResultDto>> EvaluateRoutingAsync(
        string entityType,
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> data,
        Guid? companyId = null,
        CancellationToken ct = default);
}
