namespace Liakont.Modules.Validation.Tests.Unit.Rules;

using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Validation.Contracts;
using Liakont.Modules.Validation.Domain.Rules;
using Xunit;

public sealed class BuyerLooksProfessionalRuleTests
{
    [Fact]
    public async Task No_customer_yields_no_issue()
    {
        // B2C sans tiers identifié : aucun acheteur à qualifier.
        var issues = await new BuyerLooksProfessionalRule().ValidateAsync(Context(customer: null));

        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Plain_individual_buyer_yields_no_issue()
    {
        var customer = new PivotPartyDto("Jean Dupont");

        var issues = await new BuyerLooksProfessionalRule().ValidateAsync(Context(customer));

        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Ambiguous_name_without_legal_form_token_yields_no_issue()
    {
        // « SA » et « EI » en sous-chaîne, mais pas comme tokens : pas de blocage.
        var customer = new PivotPartyDto("Galerie Sabatier");

        var issues = await new BuyerLooksProfessionalRule().ValidateAsync(Context(customer));

        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Buyer_with_legal_form_in_name_is_blocking()
    {
        var customer = new PivotPartyDto("MARTIN SAS");

        var issues = await new BuyerLooksProfessionalRule().ValidateAsync(Context(customer));

        var issue = issues.Should().ContainSingle().Subject;
        issue.Code.Should().Be(BuyerLooksProfessionalRule.BuyerLooksProfessional);
        issue.Severity.Should().Be(ValidationSeverity.Blocking);
        issue.MessageOperateur.Should().Contain("MARTIN SAS");
        issue.MessageOperateur.Should().Contain("2019"); // n° de document
        issue.MessageOperateur.Should().Contain("professionnel");
    }

    [Fact]
    public async Task Buyer_with_present_vat_number_is_blocking()
    {
        var customer = new PivotPartyDto("Jean Dupont", vatNumber: "FR40303265045");

        var issues = await new BuyerLooksProfessionalRule().ValidateAsync(Context(customer));

        var issue = issues.Should().ContainSingle().Subject;
        issue.Code.Should().Be(BuyerLooksProfessionalRule.BuyerLooksProfessional);
        issue.Severity.Should().Be(ValidationSeverity.Blocking);
    }

    [Fact]
    public async Task Buyer_with_raw_societe_hint_is_blocking()
    {
        // Indice « société » brut transcrit par l'agent (champ source societe non vide).
        var customer = new PivotPartyDto("Jean Dupont", isCompanyHint: true);

        var issues = await new BuyerLooksProfessionalRule().ValidateAsync(Context(customer));

        var issue = issues.Should().ContainSingle().Subject;
        issue.Code.Should().Be(BuyerLooksProfessionalRule.BuyerLooksProfessional);
        issue.Severity.Should().Be(ValidationSeverity.Blocking);
    }

    [Fact]
    public async Task Amount_alone_is_never_a_criterion()
    {
        // Bordereau à montant élevé mais acheteur particulier : le montant n'est jamais un indice (F07-F08 §A.4).
        var customer = new PivotPartyDto("Jean Dupont");

        var issues = await new BuyerLooksProfessionalRule().ValidateAsync(Context(customer, totalTtc: 999_999.99m));

        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Null_context_is_rejected()
    {
        var act = async () => await new BuyerLooksProfessionalRule().ValidateAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    private static DocumentValidationContext Context(PivotPartyDto? customer, decimal totalTtc = 1160.00m)
    {
        var document = new PivotDocumentDto(
            sourceDocumentKind: "BORDEREAU",
            number: "2019",
            issueDate: new DateTime(2024, 1, 15),
            sourceReference: "src-2019",
            supplier: new PivotPartyDto("Étude Fictive SVV"),
            totals: new PivotTotalsDto(totalTtc, 0m, totalTtc),
            operationCategory: OperationCategory.LivraisonBiens,
            customer: customer);
        return new DocumentValidationContext(document, Guid.NewGuid());
    }
}
