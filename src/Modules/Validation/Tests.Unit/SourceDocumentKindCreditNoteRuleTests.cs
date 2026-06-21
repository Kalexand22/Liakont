namespace Liakont.Modules.Validation.Tests.Unit;

using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Validation.Contracts;
using Liakont.Modules.Validation.Contracts.Classification;
using Liakont.Modules.Validation.Domain.Classification;
using Liakont.Modules.Validation.Domain.Rules;
using Xunit;
using static Liakont.Modules.Validation.Tests.Unit.PivotDocumentBuilder;

public sealed class SourceDocumentKindCreditNoteRuleTests
{
    [Fact]
    public async Task Source_kind_classified_credit_note_without_origin_is_blocking()
    {
        // Le trou couvert par RD405 : la source porte la nature « avoir » par son seul type, sans référence.
        var rule = new SourceDocumentKindCreditNoteRule(new FakeClassifier(SourceDocumentClassification.CreditNote));
        var doc = Document(number: "AV-200", sourceDocumentKind: "AVOIR", creditNoteRefs: null);

        var issues = await rule.ValidateAsync(Context(doc));

        var issue = issues.Should().ContainSingle().Subject;
        issue.Code.Should().Be(SourceDocumentKindCreditNoteRule.CreditNoteKindWithoutOriginCode);
        issue.Severity.Should().Be(ValidationSeverity.Blocking);
        issue.MessageOperateur.Should().Contain("AV-200");
        issue.MessageOperateur.Should().Contain("AVOIR"); // type source cité
        issue.MessageOperateur.Should().Contain("aucune référence n'est fabriquée");
    }

    [Fact]
    public async Task Credit_note_with_origin_reference_is_left_to_CreditNoteRule()
    {
        // Avec une référence d'origine, les contrôles nominaux (CreditNoteRule) prennent le relais :
        // cette règle reste silencieuse et n'interroge même pas le classificateur (pas de double blocage).
        var classifier = new FakeClassifier(SourceDocumentClassification.CreditNote);
        var rule = new SourceDocumentKindCreditNoteRule(classifier);
        var doc = Document(number: "AV-200", sourceDocumentKind: "AVOIR", creditNoteRefs: new[] { OriginalRef("2018") });

        var issues = await rule.ValidateAsync(Context(doc));

        issues.Should().BeEmpty();
        classifier.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Source_kind_classified_invoice_produces_no_issue()
    {
        var rule = new SourceDocumentKindCreditNoteRule(new FakeClassifier(SourceDocumentClassification.Invoice));
        var doc = Document(number: "F-100", sourceDocumentKind: "FACTURE", creditNoteRefs: null);

        var issues = await rule.ValidateAsync(Context(doc));

        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Unmapped_source_kind_produces_no_issue_never_guessed()
    {
        // Type source non cartographié par le tenant → on ne devine pas (CLAUDE.md n°2) : aucun blocage,
        // le repli structurel (absence de référence ici) laisse le document suivre son cours de facture.
        var rule = new SourceDocumentKindCreditNoteRule(new FakeClassifier(SourceDocumentClassification.Unmapped));
        var doc = Document(number: "X-1", sourceDocumentKind: "BORDEREAU", creditNoteRefs: null);

        var issues = await rule.ValidateAsync(Context(doc));

        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Default_classifier_keeps_the_rule_dormant_until_a_tenant_table_is_provisioned()
    {
        // Bout-en-bout avec le DÉFAUT enregistré : tant qu'aucune table tenant n'existe, « non classé »
        // partout → aucun blocage (rétro-compatibilité, repli structurel).
        var rule = new SourceDocumentKindCreditNoteRule(new UnconfiguredSourceDocumentKindClassifier());
        var doc = Document(number: "AV-300", sourceDocumentKind: "AVOIR", creditNoteRefs: null);

        var issues = await rule.ValidateAsync(Context(doc));

        issues.Should().BeEmpty();
    }

    [Fact]
    public void Null_classifier_is_rejected()
    {
        var act = () => new SourceDocumentKindCreditNoteRule(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Cancellation_is_propagated()
    {
        var rule = new SourceDocumentKindCreditNoteRule(new FakeClassifier(SourceDocumentClassification.CreditNote));
        var doc = Document(number: "AV-200", sourceDocumentKind: "AVOIR", creditNoteRefs: null);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await rule.ValidateAsync(Context(doc), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Unconfigured_default_returns_unmapped_for_any_value()
    {
        var classifier = new UnconfiguredSourceDocumentKindClassifier();

        var result = await classifier.ClassifyAsync(Guid.NewGuid(), "AVOIR");

        result.Should().Be(SourceDocumentClassification.Unmapped);
    }

    private sealed class FakeClassifier : ISourceDocumentKindClassifier
    {
        private readonly SourceDocumentClassification _result;

        public FakeClassifier(SourceDocumentClassification result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public Task<SourceDocumentClassification> ClassifyAsync(Guid companyId, string? sourceDocumentKind, CancellationToken cancellationToken = default)
        {
            CallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_result);
        }
    }
}
