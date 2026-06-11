namespace Stratum.Modules.Job.Infrastructure;

/// <summary>
/// Marker record for DI registration of job handler payload types.
/// </summary>
/// <param name="PayloadType">The job payload type a handler is registered for.</param>
/// <param name="DisplayName">
/// Optional French, user-friendly label surfaced by <c>IJobTypeCatalog</c> in the schedule admin UI so the
/// .NET <c>FullName</c> is never shown to the operator. <c>null</c> = the catalog falls back to a humanized
/// short type name. Liakont addition (FIX211) — additive optional parameter, existing call sites unchanged.
/// </param>
public sealed record JobHandlerRegistration(Type PayloadType, string? DisplayName = null);
