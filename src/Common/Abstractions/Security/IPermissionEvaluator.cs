namespace Stratum.Common.Abstractions.Security;

public interface IPermissionEvaluator
{
    Task<bool> HasPermissionAsync(
        IActorContext actor,
        string permission,
        IDictionary<string, object?>? resourceContext = null,
        CancellationToken cancellationToken = default);
}
