namespace Stratum.Modules.Job.Contracts;

public interface IJobHandler<T>
{
    Task HandleAsync(T payload, CancellationToken ct = default);
}
