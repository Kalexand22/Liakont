namespace Liakont.Modules.Validation.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.Validation.Contracts;
using Liakont.Modules.Validation.Domain.Rules;
using Xunit;

public sealed class UniquenessRuleTests
{
    [Fact]
    public async Task Unique_number_produces_no_issue()
    {
        var rule = new UniquenessRule(new FakeIssuedDocumentLookup(alreadyIssued: false));

        var issues = await rule.ValidateAsync(TestDoc.Context(number: "F-2024-001"));

        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Already_issued_number_is_blocking()
    {
        var rule = new UniquenessRule(new FakeIssuedDocumentLookup(alreadyIssued: true));

        var issues = await rule.ValidateAsync(TestDoc.Context(number: "F-2024-001"));

        issues.Should().ContainSingle(i => i.Code == UniquenessRule.DuplicateCode)
            .Which.Severity.Should().Be(ValidationSeverity.Blocking);
    }

    [Fact]
    public async Task Missing_number_is_blocking_and_does_not_query_documents()
    {
        var fake = new FakeIssuedDocumentLookup(alreadyIssued: false);
        var rule = new UniquenessRule(fake);

        var issues = await rule.ValidateAsync(TestDoc.Context(number: "   "));

        issues.Should().ContainSingle(i => i.Code == UniquenessRule.NumberMissingCode)
            .Which.Severity.Should().Be(ValidationSeverity.Blocking);
        fake.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Lookup_is_tenant_scoped()
    {
        var companyId = Guid.NewGuid();
        var fake = new FakeIssuedDocumentLookup(alreadyIssued: false);
        var rule = new UniquenessRule(fake);

        await rule.ValidateAsync(TestDoc.Context(number: "F-2024-001", companyId: companyId));

        fake.CallCount.Should().Be(1);
        fake.CapturedCompanyId.Should().Be(companyId);
        fake.CapturedNumber.Should().Be("F-2024-001");
    }

    private sealed class FakeIssuedDocumentLookup : IIssuedDocumentLookup
    {
        private readonly bool _alreadyIssued;

        public FakeIssuedDocumentLookup(bool alreadyIssued) => _alreadyIssued = alreadyIssued;

        public Guid? CapturedCompanyId { get; private set; }

        public string? CapturedNumber { get; private set; }

        public int CallCount { get; private set; }

        public Task<bool> IsAlreadyIssuedAsync(Guid companyId, string documentNumber, CancellationToken cancellationToken = default)
        {
            CapturedCompanyId = companyId;
            CapturedNumber = documentNumber;
            CallCount++;
            return Task.FromResult(_alreadyIssued);
        }
    }
}
