namespace Liakont.Modules.Validation.Tests.Unit;

using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Validation.Contracts;
using Xunit;

public sealed class DocumentValidationContextTests
{
    [Fact]
    public void Null_document_is_rejected()
    {
        var act = () => new DocumentValidationContext(null!, Guid.NewGuid());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Empty_company_id_is_rejected()
    {
        var document = ValidDocument();

        var act = () => new DocumentValidationContext(document, Guid.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Valid_arguments_are_accepted()
    {
        var document = ValidDocument();
        var companyId = Guid.NewGuid();

        var context = new DocumentValidationContext(document, companyId);

        context.Document.Should().BeSameAs(document);
        context.CompanyId.Should().NotBeEmpty();
    }

    private static PivotDocumentDto ValidDocument() =>
        new PivotDocumentDto(
            sourceDocumentKind: "BORDEREAU",
            number: "2019",
            issueDate: new DateTime(2024, 1, 15),
            sourceReference: "src-2019",
            supplier: new PivotPartyDto("Étude Fictive SVV"),
            totals: new PivotTotalsDto(1160.00m, 0m, 1160.00m),
            operationCategory: OperationCategory.LivraisonBiens);
}
