namespace Stratum.Modules.Notification.Tests.Unit.Fakes;

using System.Text.Json;
using Stratum.Modules.Notification.Contracts;
using Stratum.Modules.Notification.Contracts.DTOs;

internal sealed class FakeRoutingEngine : IRoutingEngine
{
    public Task<IReadOnlyList<RoutingResultDto>> EvaluateRoutingAsync(
        string entityType,
        IReadOnlyDictionary<string, JsonElement> data,
        Guid? companyId,
        CancellationToken ct)
    {
        return Task.FromResult<IReadOnlyList<RoutingResultDto>>([]);
    }
}
