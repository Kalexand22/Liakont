namespace Liakont.Modules.Validation.Tests.Unit;

using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Validation.Contracts;
using Liakont.Modules.Validation.Contracts.CreditNotes;
using Liakont.Modules.Validation.Domain.Rules;
using Xunit;
using static Liakont.Modules.Validation.Tests.Unit.PivotDocumentBuilder;

public sealed class CreditNoteRuleTests
{
    [Fact]
    public async Task Non_credit_note_produces_no_issue_and_does_not_call_lookup()
    {
        var lookup = new FakeLookup(OriginalInvoiceStatus.Unknown);
        var rule = new CreditNoteRule(lookup);
        var doc = Document(lines: new[] { Line(taxes: new[] { Tax() }) }); // aucune référence d'origine

        var issues = await rule.ValidateAsync(Context(doc));

        issues.Should().BeEmpty();
        lookup.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Valid_credit_note_with_issued_original_is_ok()
    {
        var rule = new CreditNoteRule(new FakeLookup(OriginalInvoiceStatus.KnownIssued));
        var doc = Document(
            number: "AV-100",
            lines: new[] { Line(netAmount: 100m, taxes: new[] { Tax(taxAmount: 20m, rate: 20m, category: VatCategory.S) }) },
            creditNoteRefs: new[] { OriginalRef("2018") },
            totals: new PivotTotalsDto(100m, 20m, 120m));

        var issues = await rule.ValidateAsync(Context(doc));

        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Credit_note_with_unknown_original_is_orphan_blocking()
    {
        var rule = new CreditNoteRule(new FakeLookup(OriginalInvoiceStatus.Unknown));
        var doc = Document(number: "AV-100", creditNoteRefs: new[] { OriginalRef("2018") });

        var issues = await rule.ValidateAsync(Context(doc));

        var issue = issues.Should().ContainSingle().Subject;
        issue.Code.Should().Be(CreditNoteRule.OrphanCode);
        issue.Severity.Should().Be(ValidationSeverity.Blocking);
        issue.MessageOperateur.Should().Contain("AV-100");
        issue.MessageOperateur.Should().Contain("2018"); // numéro de la facture d'origine cité
    }

    [Fact]
    public async Task Credit_note_with_known_but_not_issued_original_is_blocking()
    {
        var rule = new CreditNoteRule(new FakeLookup(OriginalInvoiceStatus.KnownNotIssued));
        var doc = Document(number: "AV-100", creditNoteRefs: new[] { OriginalRef("2018") });

        var issues = await rule.ValidateAsync(Context(doc));

        issues.Should().ContainSingle(i => i.Code == CreditNoteRule.OriginalNotIssuedCode);
    }

    [Fact]
    public async Task Credit_note_with_negative_line_amount_is_blocking()
    {
        var rule = new CreditNoteRule(new FakeLookup(OriginalInvoiceStatus.KnownIssued));
        var doc = Document(
            number: "AV-100",
            lines: new[] { Line(netAmount: -100m, taxes: new[] { Tax(taxAmount: -20m, rate: 20m, category: VatCategory.S) }) },
            creditNoteRefs: new[] { OriginalRef("2018") },
            totals: new PivotTotalsDto(-100m, -20m, -120m));

        var issues = await rule.ValidateAsync(Context(doc));

        issues.Should().Contain(i => i.Code == CreditNoteRule.NegativeAmountCode);
    }

    [Fact]
    public async Task Credit_note_with_negative_total_only_is_blocking()
    {
        var rule = new CreditNoteRule(new FakeLookup(OriginalInvoiceStatus.KnownIssued));
        var doc = Document(
            number: "AV-100",
            creditNoteRefs: new[] { OriginalRef("2018") },
            totals: new PivotTotalsDto(100m, 20m, -120m));

        var issues = await rule.ValidateAsync(Context(doc));

        issues.Should().ContainSingle(i => i.Code == CreditNoteRule.NegativeAmountCode);
    }

    [Fact]
    public async Task Credit_note_with_negative_unit_price_is_blocking()
    {
        var rule = new CreditNoteRule(new FakeLookup(OriginalInvoiceStatus.KnownIssued));
        var doc = Document(
            number: "AV-100",
            lines: new[] { Line(netAmount: 100m, unitPriceNet: -100m, taxes: new[] { Tax(taxAmount: 20m, rate: 20m, category: VatCategory.S) }) },
            creditNoteRefs: new[] { OriginalRef("2018") },
            totals: new PivotTotalsDto(100m, 20m, 120m));

        var issues = await rule.ValidateAsync(Context(doc));

        issues.Should().ContainSingle(i => i.Code == CreditNoteRule.NegativeAmountCode);
    }

    [Fact]
    public async Task Credit_note_with_negative_document_charge_is_blocking()
    {
        var rule = new CreditNoteRule(new FakeLookup(OriginalInvoiceStatus.KnownIssued));
        var doc = Document(
            number: "AV-100",
            creditNoteRefs: new[] { OriginalRef("2018") },
            totals: new PivotTotalsDto(100m, 20m, 120m),
            documentCharges: new[] { new PivotDocumentChargeDto(isCharge: true, amount: -5m, reason: "éco-contribution") });

        var issues = await rule.ValidateAsync(Context(doc));

        issues.Should().ContainSingle(i => i.Code == CreditNoteRule.NegativeAmountCode);
    }

    [Fact]
    public async Task Credit_note_with_negative_payment_is_blocking()
    {
        var rule = new CreditNoteRule(new FakeLookup(OriginalInvoiceStatus.KnownIssued));
        var doc = Document(
            number: "AV-100",
            creditNoteRefs: new[] { OriginalRef("2018") },
            totals: new PivotTotalsDto(100m, 20m, 120m),
            payments: new[] { new PivotPaymentDto(new DateTime(2024, 1, 12), -50m) });

        var issues = await rule.ValidateAsync(Context(doc));

        issues.Should().ContainSingle(i => i.Code == CreditNoteRule.NegativeAmountCode);
    }

    [Fact]
    public async Task Credit_note_with_blank_reference_number_is_blocking()
    {
        var rule = new CreditNoteRule(new FakeLookup(OriginalInvoiceStatus.KnownIssued));
        var doc = Document(number: "AV-100", creditNoteRefs: new[] { OriginalRef("   ") });

        var issues = await rule.ValidateAsync(Context(doc));

        issues.Should().ContainSingle(i => i.Code == CreditNoteRule.ReferenceMissingCode);
    }

    [Fact]
    public async Task Grouped_credit_note_flags_only_the_unresolved_reference()
    {
        var lookup = new FakeLookup(new Dictionary<string, OriginalInvoiceStatus>
        {
            ["2018"] = OriginalInvoiceStatus.KnownIssued,
            ["2017"] = OriginalInvoiceStatus.Unknown,
        });
        var rule = new CreditNoteRule(lookup);
        var doc = Document(number: "AV-100", creditNoteRefs: new[] { OriginalRef("2018"), OriginalRef("2017") });

        var issues = await rule.ValidateAsync(Context(doc));

        var issue = issues.Should().ContainSingle().Subject;
        issue.Code.Should().Be(CreditNoteRule.OrphanCode);
        issue.MessageOperateur.Should().Contain("2017");
    }

    [Fact]
    public void Null_lookup_is_rejected()
    {
        var act = () => new CreditNoteRule(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Cancellation_is_propagated()
    {
        var rule = new CreditNoteRule(new FakeLookup(OriginalInvoiceStatus.KnownIssued));
        var doc = Document(number: "AV-100", creditNoteRefs: new[] { OriginalRef("2018") });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await rule.ValidateAsync(Context(doc), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class FakeLookup : IIssuedInvoiceLookup
    {
        private readonly OriginalInvoiceStatus _fixedStatus;
        private readonly IReadOnlyDictionary<string, OriginalInvoiceStatus>? _byNumber;

        public FakeLookup(OriginalInvoiceStatus fixedStatus)
        {
            _fixedStatus = fixedStatus;
        }

        public FakeLookup(IReadOnlyDictionary<string, OriginalInvoiceStatus> byNumber)
        {
            _byNumber = byNumber;
        }

        public int CallCount { get; private set; }

        public Task<OriginalInvoiceStatus> FindOriginalStatusAsync(Guid companyId, PivotDocumentRefDto originalReference, CancellationToken cancellationToken = default)
        {
            CallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            if (_byNumber is not null)
            {
                return Task.FromResult(_byNumber.TryGetValue(originalReference.Number, out var status) ? status : OriginalInvoiceStatus.Unknown);
            }

            return Task.FromResult(_fixedStatus);
        }
    }
}
