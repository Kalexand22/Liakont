namespace Liakont.Modules.Validation.Tests.Unit.Rules;

using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Validation.Contracts;
using Liakont.Modules.Validation.Domain.Rules;
using Xunit;

public sealed class PartyRoleConsistencyRuleTests
{
    // SIREN fictif valide (clé de Luhn) — jamais une donnée client (CLAUDE.md n°7).
    private const string ValidInvoicerSiren = "404833048";

    [Fact]
    public async Task Standard_document_yields_no_issue()
    {
        // Ni auto-facturation, ni émetteur matériel, ni bénéficiaire de paiement : aucun contrôle déclenché.
        var issues = await new PartyRoleConsistencyRule().ValidateAsync(Context(Document()));

        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Self_billed_with_identified_invoicer_yields_no_issue()
    {
        var doc = Document(
            isSelfBilled: true,
            invoicer: new PivotPartyDto("Étude Mandataire Fictïve", siren: ValidInvoicerSiren));

        var issues = await new PartyRoleConsistencyRule().ValidateAsync(Context(doc));

        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Self_billed_without_invoicer_is_blocking()
    {
        // Acceptance RD404 : « 389 sans Invoicer ».
        var doc = Document(isSelfBilled: true, invoicer: null);

        var issues = await new PartyRoleConsistencyRule().ValidateAsync(Context(doc));

        var issue = issues.Should().ContainSingle().Subject;
        issue.Code.Should().Be(PartyRoleConsistencyRule.SelfBilledInvoicerMissing);
        issue.Severity.Should().Be(ValidationSeverity.Blocking);
        issue.MessageOperateur.Should().Contain("2019"); // numéro de document
    }

    [Fact]
    public async Task Self_billed_with_invoicer_missing_siren_is_blocking()
    {
        // Acceptance RD404 : « SIREN Invoicer manquant ».
        var doc = Document(isSelfBilled: true, invoicer: new PivotPartyDto("Étude Mandataire Fictïve"));

        var issues = await new PartyRoleConsistencyRule().ValidateAsync(Context(doc));

        var issue = issues.Should().ContainSingle().Subject;
        issue.Code.Should().Be(PartyRoleConsistencyRule.SelfBilledInvoicerUnidentified);
        issue.Severity.Should().Be(ValidationSeverity.Blocking);
        issue.MessageOperateur.Should().Contain("2019");
    }

    [Fact]
    public async Task Self_billed_with_invoicer_invalid_siren_is_blocking()
    {
        // SIREN présent mais échouant la clé de Luhn → émetteur matériel non identifié.
        var doc = Document(
            isSelfBilled: true,
            invoicer: new PivotPartyDto("Étude Mandataire Fictïve", siren: "123456789"));

        var issues = await new PartyRoleConsistencyRule().ValidateAsync(Context(doc));

        var issue = issues.Should().ContainSingle().Subject;
        issue.Code.Should().Be(PartyRoleConsistencyRule.SelfBilledInvoicerUnidentified);
        issue.Severity.Should().Be(ValidationSeverity.Blocking);
        issue.MessageOperateur.Should().Contain("123456789");
    }

    [Fact]
    public async Task Self_billed_with_sandbox_test_invoicer_siren_is_gated_by_environment()
    {
        // BUG-23 : l'émetteur matériel (mandataire 389) avec un SIREN de test sandbox PA (000000002, Luhn invalide)
        // est BLOQUANT en production (contexte par défaut, strict) et IDENTIFIÉ hors production (contexte PA Sandbox)
        // — même gating que l'acheteur, jamais affaiblissement silencieux (CLAUDE.md n°3).
        var doc = Document(
            isSelfBilled: true,
            invoicer: new PivotPartyDto("Étude Mandataire Sandbox", siren: "000000002"));

        var blockedInProduction = await new PartyRoleConsistencyRule().ValidateAsync(Context(doc));
        blockedInProduction.Should().ContainSingle(issue => issue.Code == PartyRoleConsistencyRule.SelfBilledInvoicerUnidentified)
            .Which.Severity.Should().Be(ValidationSeverity.Blocking);

        var identifiedInSandbox = await new PartyRoleConsistencyRule().ValidateAsync(Context(doc, allowSandboxTestIdentifiers: true));
        identifiedInSandbox.Should().BeEmpty("hors production, le SIREN de test sandbox PA identifie l'émetteur matériel (recette).");
    }

    [Fact]
    public async Task Invoicer_without_self_billed_is_blocking()
    {
        // Acceptance RD404 : « Invoicer sans IsSelfBilled ».
        var doc = Document(
            isSelfBilled: false,
            invoicer: new PivotPartyDto("Étude Mandataire Fictïve", siren: ValidInvoicerSiren));

        var issues = await new PartyRoleConsistencyRule().ValidateAsync(Context(doc));

        var issue = issues.Should().ContainSingle().Subject;
        issue.Code.Should().Be(PartyRoleConsistencyRule.InvoicerWithoutSelfBilled);
        issue.Severity.Should().Be(ValidationSeverity.Blocking);
        issue.MessageOperateur.Should().Contain("2019");
    }

    [Fact]
    public async Task Payee_present_is_a_warning_not_blocking()
    {
        // Décision RD404 : Payee (BG-10, affacturage) = différé explicite, signalé (Warning), jamais bloquant.
        var doc = Document(payee: new PivotPartyDto("Société d'affacturage fictive", siren: ValidInvoicerSiren));

        var issues = await new PartyRoleConsistencyRule().ValidateAsync(Context(doc));

        var issue = issues.Should().ContainSingle().Subject;
        issue.Code.Should().Be(PartyRoleConsistencyRule.PayeeNotTransmitted);
        issue.Severity.Should().Be(ValidationSeverity.Warning);
        issue.MessageOperateur.Should().Contain("2019");
    }

    [Fact]
    public async Task Self_billing_anomaly_and_payee_warning_are_both_reported()
    {
        // Les anomalies d'auto-facturation et le signalement Payee sont cumulables.
        var doc = Document(
            isSelfBilled: true,
            invoicer: null,
            payee: new PivotPartyDto("Société d'affacturage fictive", siren: ValidInvoicerSiren));

        var issues = await new PartyRoleConsistencyRule().ValidateAsync(Context(doc));

        issues.Should().HaveCount(2);
        issues.Select(i => i.Code).Should().Contain(new[]
        {
            PartyRoleConsistencyRule.SelfBilledInvoicerMissing,
            PartyRoleConsistencyRule.PayeeNotTransmitted,
        });
    }

    [Fact]
    public async Task Null_context_is_rejected()
    {
        var act = async () => await new PartyRoleConsistencyRule().ValidateAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    private static DocumentValidationContext Context(PivotDocumentDto document, bool allowSandboxTestIdentifiers = false)
        => new(document, Guid.NewGuid(), allowSandboxTestIdentifiers: allowSandboxTestIdentifiers);

    private static PivotDocumentDto Document(
        bool isSelfBilled = false,
        PivotPartyDto? invoicer = null,
        PivotPartyDto? payee = null)
        => new(
            sourceDocumentKind: "BORDEREAU",
            number: "2019",
            issueDate: new DateTime(2024, 1, 15),
            sourceReference: "src-2019",
            supplier: new PivotPartyDto("Étude Fictive SVV", siren: ValidInvoicerSiren),
            totals: new PivotTotalsDto(120.00m, 24.00m, 144.00m),
            operationCategory: OperationCategory.LivraisonBiens,
            invoicer: invoicer,
            payee: payee,
            isSelfBilled: isSelfBilled);
}
