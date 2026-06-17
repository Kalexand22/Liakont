namespace Liakont.Modules.Mandats.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.DocumentApproval.Contracts.DTOs;
using Liakont.Modules.Mandats.Contracts.DTOs;
using Liakont.Modules.Mandats.Contracts.Queries;
using Liakont.Modules.Mandats.Infrastructure;
using Xunit;

/// <summary>
/// Garde d'émission self-billed (MND03, ADR-0024 §3 / INV-ACCEPT-2). Depuis SIG06, l'émissibilité est tranchée
/// par la Règle de gate GÉNÉRIQUE (<see cref="IDocumentApprovalGate"/>, ADR-0028 §5 : état × niveau requis tenant ×
/// forme expresse) pour le purpose <see cref="ValidationPurpose.SelfBilledAcceptance"/> — la règle n'est PAS
/// dupliquée ici. L'état fiscal d'acceptation reste lu via la projection pour le message opérateur. Fail-closed.
/// </summary>
public sealed class SelfBilledGateTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Emission_Follows_The_Generic_Gate_Decision(bool gateOpen)
    {
        var gate = new StubApprovalGate(gateOpen);
        var sut = new SelfBilledGate(gate, new StubAcceptanceQueries(Acceptance("Accepted", isAccepted: true)));

        var verdict = await sut.EvaluateEmissionAsync(Guid.NewGuid(), Guid.NewGuid());

        verdict.IsEmissionAllowed.Should().Be(gateOpen, "l'émissibilité suit la Règle de gate générique (SIG06)");
    }

    [Fact]
    public async Task Delegates_To_The_SelfBilledAcceptance_Purpose_And_Surfaces_The_Acceptance_State()
    {
        var gate = new StubApprovalGate(isOpen: true);
        var sut = new SelfBilledGate(gate, new StubAcceptanceQueries(Acceptance("TacitlyAccepted", isAccepted: true)));

        var verdict = await sut.EvaluateEmissionAsync(Guid.NewGuid(), Guid.NewGuid());

        gate.LastPurpose.Should().Be(ValidationPurpose.SelfBilledAcceptance, "le gate est interrogé pour le purpose self-billing");
        verdict.AcceptanceState.Should().Be("TacitlyAccepted", "l'état fiscal d'acceptation est exposé pour le message opérateur");
    }

    [Fact]
    public async Task Missing_Acceptance_Record_Leaves_State_Null_And_FailsClosed()
    {
        // Aucune validation enregistrée : le gate générique est fermé (fail-closed) ET l'état d'acceptation est null.
        var sut = new SelfBilledGate(new StubApprovalGate(isOpen: false), new StubAcceptanceQueries(acceptance: null));

        var verdict = await sut.EvaluateEmissionAsync(Guid.NewGuid(), Guid.NewGuid());

        verdict.IsEmissionAllowed.Should().BeFalse("absence de validation ⇒ émission bloquée (fail-closed)");
        verdict.AcceptanceState.Should().BeNull();
    }

    [Fact]
    public async Task Scopes_Both_Reads_By_Company_And_Document()
    {
        var companyId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var gate = new StubApprovalGate(isOpen: true);
        var queries = new StubAcceptanceQueries(Acceptance("Accepted", isAccepted: true));
        var sut = new SelfBilledGate(gate, queries);

        await sut.EvaluateEmissionAsync(companyId, documentId);

        gate.LastCompanyId.Should().Be(companyId);
        gate.LastDocumentId.Should().Be(documentId);
        queries.LastCompanyId.Should().Be(companyId);
        queries.LastDocumentId.Should().Be(documentId);
    }

    private static SelfBilledAcceptanceDto Acceptance(string state, bool isAccepted) => new()
    {
        DocumentId = Guid.NewGuid(),
        State = state,
        PendingSince = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
        IsAccepted = isAccepted,
    };

    /// <summary>Stub du gate générique : renvoie l'ouverture configurée et mémorise le purpose + les clés interrogées.</summary>
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

    /// <summary>Stub de lecture : renvoie l'acceptation configurée et mémorise les clés de scoping interrogées.</summary>
    private sealed class StubAcceptanceQueries : ISelfBilledAcceptanceQueries
    {
        private readonly SelfBilledAcceptanceDto? _acceptance;

        public StubAcceptanceQueries(SelfBilledAcceptanceDto? acceptance) => _acceptance = acceptance;

        public Guid? LastCompanyId { get; private set; }

        public Guid? LastDocumentId { get; private set; }

        public Task<SelfBilledAcceptanceDto?> GetAcceptance(Guid companyId, Guid documentId, CancellationToken ct = default)
        {
            LastCompanyId = companyId;
            LastDocumentId = documentId;
            return Task.FromResult(_acceptance);
        }

        public Task<IReadOnlyList<SelfBilledAcceptanceLogEntryDto>> GetAcceptanceLog(Guid companyId, Guid documentId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
