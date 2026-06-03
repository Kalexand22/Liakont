namespace Stratum.Common.Infrastructure.CrossTenant;

using System.Text.Json;

/// <summary>
/// Shared JSON serialization options for cross-tenant event payloads.
/// Used by both <see cref="CrossTenantPublisher"/> and <see cref="CrossTenantHandlerRegistry"/>
/// to ensure consistent serialization/deserialization.
/// </summary>
internal static class CrossTenantJsonOptions
{
    internal static readonly JsonSerializerOptions Instance = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
