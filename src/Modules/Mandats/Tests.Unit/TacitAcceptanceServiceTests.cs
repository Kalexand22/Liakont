namespace Liakont.Modules.Mandats.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.DocumentApproval.Contracts.DTOs;
using Liakont.Modules.DocumentApproval.Contracts.Queries;
using Liakont.Modules.Mandats.Infrastructure.TacitAcceptance;
using Xunit;

/// <summary>
/// SIG05 — la bascule tacite (MND04, ADR-0024 §4) est PROJETÉE via DocumentApproval. Le service énumère les
/// candidats dus (<see cref="IDocumentApprovalQueries.ListTacitDueDocumentsAsync"/>, purpose SelfBilledAcceptance)
/// puis délègue la bascule conditionnelle à <see cref="IDocumentApprovalWorkflow.RecordTacitValidationIfDueAsync"/>
/// (re-vérification d'éligibilité SOUS VERROU = responsabilité de DocumentApproval, anti-TOCTOU). Le service ne
/// COMPTE que les bascules réellement effectuées (retour <c>true</c>) ; il transmet le purpose self-billing,
/// l'horloge figée et le libellé système (origine sans opérateur humain). La mécanique de verrou/journal est
/// couverte par les tests d'intégration DocumentApproval + Mandats.
/// </summary>
public sealed class TacitAcceptanceServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ProcessDue_Counts_Only_Documents_That_Actually_Transitioned()
    {
        var transitioned = Guid.NewGuid();
        var transitioned2 = Guid.NewGuid();
        var notDue = Guid.NewGuid(); // re-vérifié non éligible sous verrou (devenu terminal / échéance non échue)

        var queries = new FakeApprovalQueries(
        [
            new TacitDueDocumentDto { CompanyId = Guid.NewGuid(), DocumentId = transitioned },
            new TacitDueDocumentDto { CompanyId = Guid.NewGuid(), DocumentId = transitioned2 },
            new TacitDueDocumentDto { CompanyId = Guid.NewGuid(), DocumentId = notDue },
        ]);
        var workflow = new FakeWorkflow(documentsThatTransition: [transitioned, transitioned2]);

        var service = new TacitAcceptanceService(queries, workflow, new FixedTimeProvider(Now));
        var result = await service.ProcessDueAsync();

        result.Evaluated.Should().Be(3, "les trois candidats dus sont énumérés.");
        result.TacitlyAccepted.Should().Be(2, "seuls les documents réellement basculés sous verrou sont comptés.");

        queries.LastPurpose.Should().Be(ValidationPurpose.SelfBilledAcceptance);
        queries.LastNow.Should().Be(Now);

        workflow.Calls.Should().HaveCount(3);
        workflow.Calls.Should().OnlyContain(
            c => c.Purpose == ValidationPurpose.SelfBilledAcceptance
                 && c.NowUtc == Now
                 && c.OperatorName == TacitAcceptanceService.TacitOperatorName,
            "chaque bascule cible le purpose self-billing, l'horloge figée et l'origine système (sans opérateur).");
    }

    [Fact]
    public async Task ProcessDue_NoCandidates_Does_Nothing()
    {
        var queries = new FakeApprovalQueries([]);
        var workflow = new FakeWorkflow(documentsThatTransition: []);

        var service = new TacitAcceptanceService(queries, workflow, new FixedTimeProvider(Now));
        var result = await service.ProcessDueAsync();

        result.Evaluated.Should().Be(0);
        result.TacitlyAccepted.Should().Be(0);
        workflow.Calls.Should().BeEmpty();
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class FakeApprovalQueries : IDocumentApprovalQueries
    {
        private readonly IReadOnlyList<TacitDueDocumentDto> _due;

        public FakeApprovalQueries(IReadOnlyList<TacitDueDocumentDto> due) => _due = due;

        public ValidationPurpose? LastPurpose { get; private set; }

        public DateTimeOffset? LastNow { get; private set; }

        public Task<IReadOnlyList<TacitDueDocumentDto>> ListTacitDueDocumentsAsync(
            ValidationPurpose purpose, DateTimeOffset nowUtc, CancellationToken ct = default)
        {
            LastPurpose = purpose;
            LastNow = nowUtc;
            return Task.FromResult(_due);
        }

        public Task<DocumentValidationDto?> GetLatestAttempt(
            Guid companyId, Guid documentId, ValidationPurpose purpose, CancellationToken ct = default)
            => Task.FromResult<DocumentValidationDto?>(null);

        public Task<IReadOnlyList<DocumentApprovalLogEntryDto>> GetApprovalLog(
            Guid companyId, Guid documentId, ValidationPurpose purpose, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DocumentApprovalLogEntryDto>>([]);
    }

    private sealed record TacitCall(Guid CompanyId, Guid DocumentId, ValidationPurpose Purpose, DateTimeOffset NowUtc, string? OperatorName);

    private sealed class FakeWorkflow : IDocumentApprovalWorkflow
    {
        private readonly HashSet<Guid> _documentsThatTransition;

        public FakeWorkflow(IEnumerable<Guid> documentsThatTransition)
            => _documentsThatTransition = [.. documentsThatTransition];

        public List<TacitCall> Calls { get; } = [];

        public Task<bool> RecordTacitValidationIfDueAsync(
            Guid companyId, Guid documentId, ValidationPurpose purpose, DateTimeOffset nowUtc, string? operatorName, CancellationToken ct = default)
        {
            Calls.Add(new TacitCall(companyId, documentId, purpose, nowUtc, operatorName));
            return Task.FromResult(_documentsThatTransition.Contains(documentId));
        }

        public Task RequestValidationAsync(Guid companyId, Guid documentId, ValidationPurpose purpose, DateTimeOffset? deadlineUtc, Guid? operatorId, string? operatorName, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RecordRecordedValidationAsync(Guid companyId, Guid documentId, ValidationPurpose purpose, Guid? operatorId, string? operatorName, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task ContestAsync(Guid companyId, Guid documentId, ValidationPurpose purpose, Guid? operatorId, string? operatorName, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
