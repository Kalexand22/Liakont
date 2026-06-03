namespace Stratum.Modules.Job.Infrastructure;

/// <summary>
/// Marker record for DI registration of job handler payload types.
/// </summary>
public sealed record JobHandlerRegistration(Type PayloadType);
