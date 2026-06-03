namespace Stratum.Modules.Identity.Infrastructure.Security;

using Stratum.Common.Abstractions.Security;
using Stratum.Modules.Identity.Contracts.Queries;
using Stratum.Modules.Identity.Domain.Services;

internal sealed class PermissionEvaluator : IPermissionEvaluator
{
    private readonly IIdentityQueries _identityQueries;

    public PermissionEvaluator(IIdentityQueries identityQueries)
    {
        _identityQueries = identityQueries;
    }

    public async Task<bool> HasPermissionAsync(
        IActorContext actor,
        string permission,
        IDictionary<string, object?>? resourceContext = null,
        CancellationToken cancellationToken = default)
    {
        var grants = await _identityQueries.GetUserGrantsForPermission(
            actor.UserId, permission, cancellationToken);

        if (grants.Count == 0)
        {
            return false;
        }

        foreach (var grant in grants)
        {
            // Grant without condition → always authorized (backward-compatible)
            if (grant.Condition is null)
            {
                return true;
            }

            // Grant with condition → evaluate. Any matching grant = authorized.
            if (ConditionParser.Evaluate(grant.Condition, actor, resourceContext))
            {
                return true;
            }
        }

        return false;
    }
}
