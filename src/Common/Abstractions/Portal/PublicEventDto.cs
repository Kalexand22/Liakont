namespace Stratum.Common.Abstractions.Portal;

/// <summary>
/// A public listing visible on the cross-tenant portal.
/// Currently backed by parties with <c>is_public = true</c>.
/// </summary>
public sealed record PublicEventDto
{
    public required Guid EntityId { get; init; }

    public required string Title { get; init; }

    public required DateTimeOffset Date { get; init; }

    public string? Description { get; init; }

    public required string TenantId { get; init; }

    public required string TenantDisplayName { get; init; }

    /// <summary>
    /// Entity type (e.g. "Party"). Allows the portal to distinguish
    /// different kinds of public listings as more modules expose data.
    /// </summary>
    public required string Type { get; init; }
}
