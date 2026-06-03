namespace Stratum.Common.Abstractions.CrossTenant;

/// <summary>
/// Module-side handler for a specific cross-tenant event type.
/// The handler runs in the context of the target tenant's database.
/// </summary>
/// <typeparam name="TPayload">
/// The strongly-typed payload model. The infrastructure deserializes the
/// <see cref="CrossTenantEnvelope.Payload"/> (<see cref="System.Text.Json.JsonElement"/>)
/// into <typeparamref name="TPayload"/> before calling <see cref="HandleAsync"/>.
/// </typeparam>
public interface ICrossTenantHandler<in TPayload>
{
    /// <summary>
    /// The event type this handler processes.
    /// Must follow the convention <c>{Module}.{Aggregate}.{Verb}</c> (PascalCase, 3 segments).
    /// </summary>
    string EventType { get; }

    /// <summary>
    /// Handles a delivered cross-tenant event within the target tenant's context.
    /// Implementations should be idempotent (at-least-once delivery guarantee).
    /// </summary>
    Task HandleAsync(
        CrossTenantEnvelope envelope,
        TPayload payload,
        CancellationToken ct);
}
