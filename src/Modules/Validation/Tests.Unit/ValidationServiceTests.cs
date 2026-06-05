namespace Liakont.Modules.Validation.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Validation.Contracts;
using Liakont.Modules.Validation.Infrastructure;
using Xunit;

/// <summary>
/// <see cref="ValidationService"/> expose le pipeline de règles à la frontière Contracts (PIP01a).
/// </summary>
public sealed class ValidationServiceTests
{
    [Fact]
    public async Task No_Rules_Yields_A_Valid_Result()
    {
        var service = new ValidationService(Array.Empty<IDocumentRule>());

        var result = await service.ValidateAsync(BuildContext());

        result.IsValid.Should().BeTrue();
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public async Task A_Blocking_Rule_Makes_The_Result_Invalid()
    {
        var service = new ValidationService(new IDocumentRule[] { new BlockingRule() });

        var result = await service.ValidateAsync(BuildContext());

        result.HasBlockingIssue.Should().BeTrue();
        result.IsValid.Should().BeFalse();
    }

    private static DocumentValidationContext BuildContext()
    {
        var supplier = new PivotPartyDto(name: "Fournisseur Fictif");
        var totals = new PivotTotalsDto(totalNet: 100.00m, totalTax: 20.00m, totalGross: 120.00m);
        var document = new PivotDocumentDto(
            sourceDocumentKind: "F",
            number: "F-1",
            issueDate: new DateTime(2026, 1, 1),
            sourceReference: "ref-1",
            supplier: supplier,
            totals: totals,
            operationCategory: OperationCategory.LivraisonBiens);

        return new DocumentValidationContext(document, Guid.NewGuid());
    }

    private sealed class BlockingRule : IDocumentRule
    {
        public string Code => "TEST_BLOCK";

        public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(DocumentValidationContext context, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ValidationIssue> issues = new[]
            {
                ValidationIssue.Blocking("TEST_BLOCK", "Document de test bloqué.", "détail technique de test"),
            };

            return Task.FromResult(issues);
        }
    }
}
