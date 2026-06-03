namespace Stratum.Modules.Job.Application;

/// <summary>
/// Resolves and invokes the appropriate IJobHandler&lt;T&gt; for a given job type name and JSON payload.
/// </summary>
public interface IJobHandlerResolver
{
    /// <summary>
    /// Returns true if a handler is registered for the given job type name.
    /// </summary>
    bool CanHandle(string jobTypeName);

    /// <summary>
    /// Deserializes the payload and invokes the registered IJobHandler&lt;T&gt;.
    /// The caller must provide a scoped <see cref="IServiceProvider"/> — handlers
    /// are typically registered as scoped services and cannot be resolved from
    /// the root provider.
    /// </summary>
    Task ExecuteAsync(
        IServiceProvider scopedServices,
        string jobTypeName,
        string payloadJson,
        CancellationToken ct = default);
}
