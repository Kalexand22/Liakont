namespace Stratum.Common.Testing;

using Stratum.Common.Abstractions.Security;

/// <summary>
/// Test stub for IActorContextAccessor. Returns a fixed actor context with a known CorrelationId.
/// Use in unit/integration tests that directly instantiate handlers.
/// </summary>
public sealed class StubActorContextAccessor : IActorContextAccessor
{
    private readonly IActorContext _context;

    public StubActorContextAccessor(Guid? correlationId = null)
    {
        _context = new StubActorContext(correlationId ?? Guid.NewGuid());
    }

    public IActorContext Current => _context;

    private sealed class StubActorContext : IActorContext
    {
        public StubActorContext(Guid correlationId)
        {
            CorrelationId = correlationId;
        }

        public Guid UserId => Guid.Empty;

        public Guid CorrelationId { get; }

        public bool IsAuthenticated => false;

        public string? DisplayName => null;

        public string? Email => null;

        public Guid? CompanyId => null;

        public string? Timezone => null;

        public string? Language => null;

        public string? TenantId => null;
    }
}
