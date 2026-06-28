namespace Liakont.Modules.Validation.Tests.Unit.Rules;

using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Validation.Contracts;
using Liakont.Modules.Validation.Domain.Rules;
using Xunit;

public sealed class BuyerIdentityRuleTests
{
    [Fact]
    public async Task No_customer_yields_no_issue()
    {
        // B2C sans tiers identifié (Customer = null) : aucun contrôle d'identité acheteur.
        var issues = await new BuyerIdentityRule().ValidateAsync(Context(customer: null));

        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Valid_buyer_siren_and_country_yields_no_issue()
    {
        var customer = new PivotPartyDto("MARTIN SARL", siren: "123456782", address: new PivotAddressDto(countryCode: "FR"));

        var issues = await new BuyerIdentityRule().ValidateAsync(Context(customer));

        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Absent_buyer_siren_and_country_yields_no_issue()
    {
        var customer = new PivotPartyDto("Acheteur particulier"); // ni SIREN ni adresse

        var issues = await new BuyerIdentityRule().ValidateAsync(Context(customer));

        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Invalid_buyer_siren_is_blocking()
    {
        var customer = new PivotPartyDto("MARTIN SARL", siren: "123456789"); // Luhn invalide

        var issues = await new BuyerIdentityRule().ValidateAsync(Context(customer));

        var issue = issues.Should().ContainSingle().Subject;
        issue.Code.Should().Be(BuyerIdentityRule.BuyerSirenInvalid);
        issue.Severity.Should().Be(ValidationSeverity.Blocking);
        issue.MessageOperateur.Should().Contain("2019");
    }

    [Theory]
    [InlineData("ZZ")] // code non assigné
    [InlineData("France")] // pas un alpha-2
    [InlineData("xk")] // user-assigned, non officiel
    public async Task Invalid_buyer_country_is_blocking(string countryCode)
    {
        var customer = new PivotPartyDto("Acheteur étranger", address: new PivotAddressDto(countryCode: countryCode));

        var issues = await new BuyerIdentityRule().ValidateAsync(Context(customer));

        var issue = issues.Should().ContainSingle().Subject;
        issue.Code.Should().Be(BuyerIdentityRule.BuyerCountryInvalid);
        issue.Severity.Should().Be(ValidationSeverity.Blocking);
    }

    [Fact]
    public async Task Invalid_siren_and_country_yield_two_blocking_issues()
    {
        var customer = new PivotPartyDto("MARTIN SARL", siren: "111111111", address: new PivotAddressDto(countryCode: "ZZ"));

        var issues = await new BuyerIdentityRule().ValidateAsync(Context(customer));

        issues.Should().HaveCount(2);
        issues.Should().OnlyContain(issue => issue.Severity == ValidationSeverity.Blocking);
        issues.Select(issue => issue.Code).Should().Contain(new[] { BuyerIdentityRule.BuyerSirenInvalid, BuyerIdentityRule.BuyerCountryInvalid });
    }

    [Fact]
    public async Task Sandbox_test_buyer_siren_is_blocking_in_production_but_allowed_in_sandbox()
    {
        // BUG-23 : un SIREN de test sandbox PA (000000001, Luhn invalide) côté acheteur est BLOQUANT en production
        // (contexte par défaut, strict) et TOLÉRÉ hors production (contexte PA Sandbox) — gating par l'environnement
        // PA, jamais affaiblissement silencieux (CLAUDE.md n°3).
        var customer = new PivotPartyDto("TRICATEL", siren: "000000001");

        var blockedInProduction = await new BuyerIdentityRule().ValidateAsync(Context(customer));
        blockedInProduction.Should().ContainSingle(issue => issue.Code == BuyerIdentityRule.BuyerSirenInvalid)
            .Which.Severity.Should().Be(ValidationSeverity.Blocking);

        var allowedInSandbox = await new BuyerIdentityRule().ValidateAsync(Context(customer, allowSandboxTestIdentifiers: true));
        allowedInSandbox.Should().BeEmpty("hors production, le SIREN de test sandbox PA est toléré (recette e-invoicing B2B).");
    }

    [Fact]
    public async Task Null_context_is_rejected()
    {
        var act = async () => await new BuyerIdentityRule().ValidateAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    private static DocumentValidationContext Context(PivotPartyDto? customer, bool allowSandboxTestIdentifiers = false)
    {
        var document = new PivotDocumentDto(
            sourceDocumentKind: "BORDEREAU",
            number: "2019",
            issueDate: new DateTime(2024, 1, 15),
            sourceReference: "src-2019",
            supplier: new PivotPartyDto("Étude Fictive SVV"),
            totals: new PivotTotalsDto(1160.00m, 0m, 1160.00m),
            operationCategory: OperationCategory.LivraisonBiens,
            customer: customer);
        return new DocumentValidationContext(document, Guid.NewGuid(), allowSandboxTestIdentifiers: allowSandboxTestIdentifiers);
    }
}
