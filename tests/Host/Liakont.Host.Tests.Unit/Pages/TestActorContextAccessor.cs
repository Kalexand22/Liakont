namespace Liakont.Host.Tests.Unit.Pages;

using System;
using Stratum.Common.Abstractions.Security;

internal sealed class TestActorContextAccessor : IActorContextAccessor
{
    public TestActorContextAccessor(string displayName = "Superviseur Test") =>
        Current = new TestActorContext(displayName);

    public IActorContext Current { get; }

    private sealed class TestActorContext : IActorContext
    {
        public TestActorContext(string displayName) => DisplayName = displayName;

        public Guid UserId => Guid.Empty;

        public Guid CorrelationId => Guid.Empty;

        public bool IsAuthenticated => true;

        public string? DisplayName { get; }

        public string? Email => null;

        public Guid? CompanyId => null;

        public string? Timezone => null;

        public string? Language => "fr";

        public string? TenantId => "tenant-test";
    }
}
