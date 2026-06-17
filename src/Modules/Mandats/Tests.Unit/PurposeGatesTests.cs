namespace Liakont.Modules.Mandats.Tests.Unit;

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.DocumentApproval.Contracts.DTOs;
using Liakont.Modules.Mandats.Contracts;
using Liakont.Modules.Mandats.Infrastructure;
using Xunit;

/// <summary>
/// Ports de gate par purpose (SIG06, ADR-0028 §4) : chaque facade délègue la Règle de gate générique au module
/// DocumentApproval (<see cref="IDocumentApprovalGate"/>) pour SON purpose et projette le verdict — la règle
/// n'est jamais dupliquée. Vérifie le purpose interrogé, le scoping (company/document) et le passe-plat du verdict.
/// </summary>
public sealed class PurposeGatesTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MandateSignatureGate_Delegates_To_The_MandateSignature_Purpose(bool open)
    {
        var gate = new StubApprovalGate(open);

        var decision = await new MandateSignatureGate(gate).EvaluateAsync(Guid.NewGuid(), Guid.NewGuid());

        gate.LastPurpose.Should().Be(ValidationPurpose.MandateSignature);
        decision.IsOpen.Should().Be(open);
        decision.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreditNoteAcceptanceGate_Delegates_To_The_CreditNoteAcceptance_Purpose(bool open)
    {
        var gate = new StubApprovalGate(open);

        var decision = await new CreditNoteAcceptanceGate(gate).EvaluateAsync(Guid.NewGuid(), Guid.NewGuid());

        gate.LastPurpose.Should().Be(ValidationPurpose.CreditNoteAcceptance);
        decision.IsOpen.Should().Be(open);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MultiPartySignatureGate_Delegates_To_The_MultiPartySignature_Purpose(bool open)
    {
        var gate = new StubApprovalGate(open);

        var decision = await new MultiPartySignatureGate(gate).EvaluateAsync(Guid.NewGuid(), Guid.NewGuid());

        gate.LastPurpose.Should().Be(ValidationPurpose.MultiPartySignature);
        decision.IsOpen.Should().Be(open);
    }

    [Fact]
    public async Task Gates_Scope_The_Evaluation_By_Company_And_Document()
    {
        var company = Guid.NewGuid();
        var document = Guid.NewGuid();
        var gate = new StubApprovalGate(isOpen: true);

        await new CreditNoteAcceptanceGate(gate).EvaluateAsync(company, document);

        gate.LastCompanyId.Should().Be(company);
        gate.LastDocumentId.Should().Be(document);
    }

    private sealed class StubApprovalGate : IDocumentApprovalGate
    {
        private readonly bool _isOpen;

        public StubApprovalGate(bool isOpen) => _isOpen = isOpen;

        public ValidationPurpose? LastPurpose { get; private set; }

        public Guid? LastCompanyId { get; private set; }

        public Guid? LastDocumentId { get; private set; }

        public Task<ApprovalGateResult> EvaluateAsync(Guid companyId, Guid documentId, ValidationPurpose purpose, CancellationToken ct = default)
        {
            LastCompanyId = companyId;
            LastDocumentId = documentId;
            LastPurpose = purpose;
            return Task.FromResult(new ApprovalGateResult { IsOpen = _isOpen, Reason = _isOpen ? "ouvert" : "fermé" });
        }
    }
}
