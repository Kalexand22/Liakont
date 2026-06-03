namespace Stratum.Common.Infrastructure.DataIsolation;

using Stratum.Common.Abstractions.Security;

internal sealed class CompanyFilter : ICompanyFilter
{
    private readonly IActorContextAccessor _actorContextAccessor;

    public CompanyFilter(IActorContextAccessor actorContextAccessor)
    {
        _actorContextAccessor = actorContextAccessor;
    }

    public Guid GetRequiredCompanyId()
    {
        var context = _actorContextAccessor.Current
            ?? throw new InvalidOperationException(
                "IActorContextAccessor.Current returned null. "
                + "Ensure the actor context is properly configured in the DI container.");

        return context.CompanyId
            ?? throw new InvalidOperationException(
                "CompanyId is required in the current context but is null. "
                + "Ensure the user has selected an active company before performing this operation.");
    }
}
