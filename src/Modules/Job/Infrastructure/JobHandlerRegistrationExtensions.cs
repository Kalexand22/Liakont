namespace Stratum.Modules.Job.Infrastructure;

using Microsoft.Extensions.DependencyInjection;
using Stratum.Modules.Job.Contracts;

public static class JobHandlerRegistrationExtensions
{
    /// <summary>
    /// Registers an IJobHandler&lt;T&gt; implementation so the JobWorker can resolve it at runtime.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="displayName">
    /// Optional French, user-friendly label exposed by <c>IJobTypeCatalog</c> in the schedule admin UI (so the
    /// .NET <c>FullName</c> is never displayed). Liakont addition (FIX211) — additive optional parameter; calls
    /// that omit it keep the previous behaviour (catalog falls back to a humanized short type name).
    /// </param>
    public static IServiceCollection AddJobHandler<TPayload, THandler>(this IServiceCollection services, string? displayName = null)
        where THandler : class, IJobHandler<TPayload>
    {
        services.AddScoped<IJobHandler<TPayload>, THandler>();
        services.AddSingleton(new JobHandlerRegistration(typeof(TPayload), displayName));
        return services;
    }
}
