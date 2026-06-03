namespace Stratum.Common.Abstractions.CrossTenant;

using Stratum.Common.Abstractions.BlobStorage;

/// <summary>
/// Publishes a cross-tenant event into the system outbox.
/// Called from within a tenant context (inter-tenant) or from portal routes (public submission).
/// </summary>
public interface ICrossTenantPublisher
{
    /// <summary>
    /// Inserts a new event into <c>outbox.cross_tenant_events</c> with status <c>pending</c>.
    /// </summary>
    /// <param name="sourceTenant">Source tenant identifier, or <c>null</c> for public submissions.</param>
    /// <param name="targetTenant">Target tenant identifier (required).</param>
    /// <param name="eventType">
    /// Event type following the convention <c>{Module}.{Aggregate}.{Verb}</c> (PascalCase, 3 segments).
    /// </param>
    /// <param name="payload">Payload object — serialized to JSON before storage.</param>
    /// <param name="blobs">Optional blob references attached to the event.</param>
    /// <param name="submitterEmail">Contact email, required when <paramref name="sourceTenant"/> is <c>null</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    Task PublishAsync(
        string? sourceTenant,
        string targetTenant,
        string eventType,
        object payload,
        IReadOnlyList<BlobReference>? blobs = null,
        string? submitterEmail = null,
        CancellationToken ct = default);
}
