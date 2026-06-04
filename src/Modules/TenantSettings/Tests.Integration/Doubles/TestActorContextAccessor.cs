namespace Liakont.Modules.TenantSettings.Tests.Integration.Doubles;

using Stratum.Common.Abstractions.Security;

/// <summary>Accesseur de contexte acteur de test : identité opérateur fixe (pour vérifier la journalisation).</summary>
internal sealed class TestActorContextAccessor : IActorContextAccessor
{
    public TestActorContextAccessor(Guid userId, Guid companyId)
    {
        Current = new TestActorContext(userId, companyId);
    }

    public IActorContext Current { get; }

    private sealed class TestActorContext : IActorContext
    {
        public TestActorContext(Guid userId, Guid companyId)
        {
            UserId = userId;
            CompanyId = companyId;
        }

        public Guid UserId { get; }

        public Guid CorrelationId { get; } = Guid.NewGuid();

        public bool IsAuthenticated => true;

        public string? DisplayName => "Test Operator";

        public string? Email => "operator@exemple.test";

        public Guid? CompanyId { get; }

        public string? Timezone => "Europe/Paris";

        public string? Language => "fr";

        public string? TenantId => CompanyId?.ToString();
    }
}
