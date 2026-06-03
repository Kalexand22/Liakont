namespace Stratum.Common.Infrastructure.Tests.Unit.DataIsolation;

using FluentAssertions;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.Infrastructure.DataIsolation;
using Xunit;

public sealed class CompanyFilterTests
{
    [Fact]
    public void GetRequiredCompanyId_Should_ReturnCompanyId_When_CompanyIdIsPresent()
    {
        var companyId = Guid.NewGuid();
        var filter = CreateFilter(companyId);

        var result = filter.GetRequiredCompanyId();

        result.Should().Be(companyId);
    }

    [Fact]
    public void GetRequiredCompanyId_Should_ThrowInvalidOperationException_When_CompanyIdIsNull()
    {
        var filter = CreateFilter(companyId: null);

        var act = () => filter.GetRequiredCompanyId();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*CompanyId*required*null*")
            .WithMessage("*active company*");
    }

    [Fact]
    public void GetRequiredCompanyId_Should_ThrowInvalidOperationException_When_CurrentIsNull()
    {
        var accessor = new NullCurrentAccessor();
        var filter = new CompanyFilter(accessor);

        var act = () => filter.GetRequiredCompanyId();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IActorContextAccessor.Current*null*");
    }

    private static CompanyFilter CreateFilter(Guid? companyId)
    {
        var accessor = new StubActorContextAccessor(companyId);
        return new CompanyFilter(accessor);
    }

    private sealed class NullCurrentAccessor : IActorContextAccessor
    {
        public IActorContext Current => null!;
    }

    private sealed class StubActorContextAccessor : IActorContextAccessor
    {
        public StubActorContextAccessor(Guid? companyId)
        {
            Current = new StubActorContext(companyId);
        }

        public IActorContext Current { get; }
    }

    private sealed class StubActorContext : IActorContext
    {
        public StubActorContext(Guid? companyId)
        {
            CompanyId = companyId;
        }

        public Guid UserId => Guid.NewGuid();

        public Guid CorrelationId => Guid.NewGuid();

        public bool IsAuthenticated => true;

        public string? DisplayName => "Test User";

        public string? Email => "test@example.com";

        public Guid? CompanyId { get; }

        public string? Timezone => null;

        public string? Language => null;

        public string? TenantId => null;
    }
}
