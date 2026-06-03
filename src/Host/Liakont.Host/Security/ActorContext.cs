namespace Liakont.Host.Security;

using Stratum.Common.Abstractions.Security;

internal sealed class ActorContext : IActorContext
{
    public Guid UserId { get; init; }

    public Guid CorrelationId { get; init; }

    public bool IsAuthenticated { get; init; }

    public string? DisplayName { get; init; }

    public string? Email { get; init; }

    public Guid? CompanyId { get; init; }

    public string? Timezone { get; init; }

    public string? Language { get; init; }

    public string? TenantId { get; init; }
}
