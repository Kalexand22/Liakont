namespace Stratum.Modules.Job.Infrastructure;

using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Modules.Job.Application;
using Stratum.Modules.Job.Contracts;

/// <summary>
/// Resolves IJobHandler&lt;T&gt; from DI by job type name.
/// Scans registered handler types at startup and caches the mapping.
/// </summary>
internal sealed class JobHandlerResolver : IJobHandlerResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,

        // Liakont (FIX211) : accepte les enums sous forme de nom (les champs typés de l'admin des jobs
        // sérialisent un enum par son nom) ET de nombre. Additif, rétro-compatible.
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ConcurrentDictionary<string, Type> _payloadTypeMap;

    public JobHandlerResolver(IEnumerable<JobHandlerRegistration> registrations)
    {
        _payloadTypeMap = new ConcurrentDictionary<string, Type>(StringComparer.Ordinal);

        foreach (var reg in registrations)
        {
            var typeName = reg.PayloadType.FullName ?? reg.PayloadType.Name;
            _payloadTypeMap.TryAdd(typeName, reg.PayloadType);
        }
    }

    public bool CanHandle(string jobTypeName)
    {
        return _payloadTypeMap.ContainsKey(jobTypeName);
    }

    public async Task ExecuteAsync(
        IServiceProvider scopedServices,
        string jobTypeName,
        string payloadJson,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scopedServices);

        if (!_payloadTypeMap.TryGetValue(jobTypeName, out var payloadType))
        {
            throw new InvalidOperationException(
                $"INV-JOB-001: No handler registered for job type '{jobTypeName}'.");
        }

        var payload = JsonSerializer.Deserialize(payloadJson, payloadType, JsonOptions)
            ?? throw new InvalidOperationException(
                $"Failed to deserialize payload for job type '{jobTypeName}'.");

        var handlerType = typeof(IJobHandler<>).MakeGenericType(payloadType);

        // Resolve from the caller's scoped provider — IJobHandler<T> is a
        // scoped service, so resolving from the root container would fail
        // service-provider validation (and pin a scoped instance forever).
        var handler = scopedServices.GetService(handlerType)
            ?? throw new InvalidOperationException(
                $"INV-JOB-001: No IJobHandler<{payloadType.Name}> registered in DI for job type '{jobTypeName}'.");

        var method = handlerType.GetMethod(nameof(IJobHandler<object>.HandleAsync))!;

        try
        {
            var task = (Task)method.Invoke(handler, [payload, ct])!;
            await task;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }
}
