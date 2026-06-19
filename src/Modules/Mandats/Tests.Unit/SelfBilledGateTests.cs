namespace Liakont.Modules.Mandats.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Mandats.Contracts.DTOs;
using Liakont.Modules.Mandats.Contracts.Queries;
using Liakont.Modules.Mandats.Infrastructure;
using Xunit;

/// <summary>
/// Garde d'émission self-billed (MND03, ADR-0024 §3 / INV-ACCEPT-2) : l'émission n'est autorisée QUE si
/// l'acceptation est ouverte (<c>Accepted</c> / <c>TacitlyAccepted</c>). Tout autre cas — en attente,
/// contestée, ou AUCUN enregistrement — bloque (fail-closed, « bloquer plutôt qu'émettre faux », CLAUDE.md n°3).
/// La règle « gate ouvert » n'est pas dupliquée : elle est portée par <see cref="SelfBilledAcceptanceDto.IsAccepted"/>.
/// </summary>
public sealed class SelfBilledGateTests
{
    [Theory]
    [InlineData("Accepted", true)]
    [InlineData("TacitlyAccepted", true)]
    public async Task Open_States_Allow_Emission(string state, bool isAccepted)
    {
        var gate = new SelfBilledGate(new StubAcceptanceQueries(Acceptance(state, isAccepted)));

        var verdict = await gate.EvaluateEmissionAsync(Guid.NewGuid(), Guid.NewGuid());

        verdict.IsEmissionAllowed.Should().BeTrue($"l'état « {state} » ouvre le gate d'émission");
        verdict.AcceptanceState.Should().Be(state);
    }

    [Theory]
    [InlineData("PendingAcceptance")]
    [InlineData("Contested")]
    public async Task Non_Accepted_States_Block_Emission(string state)
    {
        var gate = new SelfBilledGate(new StubAcceptanceQueries(Acceptance(state, isAccepted: false)));

        var verdict = await gate.EvaluateEmissionAsync(Guid.NewGuid(), Guid.NewGuid());

        verdict.IsEmissionAllowed.Should().BeFalse($"l'état « {state} » ne franchit pas la garde {{Accepted, TacitlyAccepted}}");
        verdict.AcceptanceState.Should().Be(state);
    }

    [Fact]
    public async Task Missing_Acceptance_Record_Blocks_Emission_FailClosed()
    {
        // Aucune acceptation enregistrée pour ce document : fail-closed (jamais émis sans acceptation).
        var gate = new SelfBilledGate(new StubAcceptanceQueries(acceptance: null));

        var verdict = await gate.EvaluateEmissionAsync(Guid.NewGuid(), Guid.NewGuid());

        verdict.IsEmissionAllowed.Should().BeFalse("absence d'acceptation ⇒ émission bloquée (fail-closed)");
        verdict.AcceptanceState.Should().BeNull();
    }

    [Fact]
    public async Task Scopes_Read_By_Company_And_Document()
    {
        var companyId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var queries = new StubAcceptanceQueries(Acceptance("Accepted", isAccepted: true));
        var gate = new SelfBilledGate(queries);

        await gate.EvaluateEmissionAsync(companyId, documentId);

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
