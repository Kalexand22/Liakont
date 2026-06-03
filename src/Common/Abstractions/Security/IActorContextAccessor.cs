namespace Stratum.Common.Abstractions.Security;

public interface IActorContextAccessor
{
    IActorContext Current { get; }
}
