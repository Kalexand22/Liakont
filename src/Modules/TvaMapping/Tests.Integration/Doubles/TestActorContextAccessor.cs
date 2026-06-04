namespace Liakont.Modules.TvaMapping.Tests.Integration.Doubles;

using Stratum.Common.Abstractions.Security;

/// <summary>
/// Accesseur de contexte acteur de test : identité opérateur (Keycloak) et tenant fixes — sert à
/// vérifier la journalisation (qui a modifié) et l'isolation par société (item TVA05 §3/§6).
/// </summary>
internal sealed class TestActorContextAccessor : IActorContextAccessor
{
    public TestActorContextAccessor(Guid userId, Guid companyId, string? displayName = "Comptable de test")
    {
        Current = new TestActorContext(userId, companyId, displayName);
    }

    public IActorContext Current { get; }

    private sealed class TestActorContext : IActorContext
    {
        public TestActorContext(Guid userId, Guid companyId, string? displayName)
        {
            UserId = userId;
            CompanyId = companyId;
            DisplayName = displayName;
        }

        public Guid UserId { get; }

        public Guid CorrelationId { get; } = Guid.NewGuid();

        public bool IsAuthenticated => true;

        public string? DisplayName { get; }

        public string? Email => "comptable@exemple.test";

        public Guid? CompanyId { get; }

        public string? Timezone => "Europe/Paris";

        public string? Language => "fr";

        public string? TenantId => CompanyId?.ToString();
    }
}
