namespace Stratum.Common.Abstractions.CrossTenant;

using System.Text.Json;
using Stratum.Common.Abstractions.BlobStorage;

/// <summary>
/// Immutable envelope for a cross-tenant event read from the system outbox.
/// Carries the full payload as a <see cref="JsonElement"/> so handlers can
/// deserialize to their own strongly-typed model.
/// </summary>
public sealed record CrossTenantEnvelope(
    Guid Id,
    string? SourceTenant,
    string TargetTenant,
    string EventType,
    JsonElement Payload,
    IReadOnlyList<BlobReference>? Blobs,
    string? SubmitterEmail,
    DateTimeOffset CreatedAt);
