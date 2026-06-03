namespace Stratum.Common.Abstractions.Security;

public interface IActorContext
{
    Guid UserId { get; }

    Guid CorrelationId { get; }

    bool IsAuthenticated { get; }

    string? DisplayName { get; }

    string? Email { get; }

    Guid? CompanyId { get; }

    string? Timezone { get; }

    string? Language { get; }

    string? TenantId { get; }
}
