namespace Stratum.Modules.Job.Infrastructure;

using Microsoft.Extensions.DependencyInjection;
using Stratum.Modules.Job.Contracts;

public static class JobHandlerRegistrationExtensions
{
    /// <summary>
    /// Registers an IJobHandler&lt;T&gt; implementation so the JobWorker can resolve it at runtime.
    /// </summary>
    public static IServiceCollection AddJobHandler<TPayload, THandler>(this IServiceCollection services)
        where THandler : class, IJobHandler<TPayload>
    {
        services.AddScoped<IJobHandler<TPayload>, THandler>();
        services.AddSingleton(new JobHandlerRegistration(typeof(TPayload)));
        return services;
    }
}
