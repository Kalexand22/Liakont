namespace Stratum.Common.Abstractions.CrossTenant;

using System.Text.Json;

/// <summary>
/// Resolves the appropriate handler for a given cross-tenant event type.
/// Populated via DI assembly scanning at startup.
/// </summary>
public interface ICrossTenantHandlerRegistry
{
    /// <summary>
    /// Returns the handler registered for <paramref name="eventType"/>,
    /// or <c>null</c> if no handler is registered.
    /// The returned handler accepts <see cref="JsonElement"/> payloads and is
    /// responsible for deserializing to the concrete type internally.
    /// </summary>
    ICrossTenantHandler<JsonElement>? Resolve(string eventType);
}
