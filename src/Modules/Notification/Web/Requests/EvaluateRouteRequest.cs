namespace Stratum.Modules.Notification.Web.Requests;

using System.Text.Json;

public record EvaluateRouteRequest
{
    public required string EntityType { get; init; }

    public required Dictionary<string, JsonElement> Data { get; init; }

    public Guid? CompanyId { get; init; }
}
