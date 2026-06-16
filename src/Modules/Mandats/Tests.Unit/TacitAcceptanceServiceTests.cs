namespace Liakont.Modules.Mandats.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Mandats.Application;
using Liakont.Modules.Mandats.Domain.Entities;
using Liakont.Modules.Mandats.Infrastructure.TacitAcceptance;
using Xunit;

/// <summary>
/// Décision de bascule tacite du service (MND04, ADR-0024 §4 / INV-ACCEPT-3), re-vérifiée sous verrou. La
/// condition « mandat écrit ET délai non null » est portée par <c>DeadlineUtc</c> (non null ⟺ possible) ;
/// le service n'ajoute que « now ≥ DeadlineUtc ». Produit cartésien tacite/écrit × délai null/non-null
/// (≡ DeadlineUtc null/non-null) × échue/non-échue, plus la défense anti-TOCTOU (état devenu terminal entre
/// l'énumération et le verrou). Horloge figée, persistance simulée — la mécanique réelle est couverte par
/// les tests d'intégration.
/// </summary>
public sealed class TacitAcceptanceServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PendingSince = Now.AddDays(-40);

    [Fact]
    public async Task ProcessDue_TacitlyAccepts_Only_Pending_With_Elapsed_Deadline()
    {
        var store = new FakeAcceptanceStore();

        // Mandat écrit + délai → DeadlineUtc non null ; échéance échue → bascule.
        var due = store.SeedPending(deadline: Now.AddDays(-1));

        // Échéance EXACTEMENT à `now` → échue (deadline <= now) → bascule (test de borne).
        var dueOnBoundary = store.SeedPending(deadline: Now);

        // Mandat tacite OU délai null → DeadlineUtc null → bascule tacite impossible.
        var noDeadline = store.SeedPending(deadline: null);

        // Mandat écrit, délai non null, mais échéance NON échue → pas encore de bascule.
        var future = store.SeedPending(deadline: Now.AddDays(1));

        var service = new TacitAcceptanceService(store.Reader, store.UowFactory, new FixedTimeProvider(Now));
        var result = await service.ProcessDueAsync();

        result.TacitlyAccepted.Should().Be(2, "seules les deux acceptations en attente à échéance échue basculent.");
        store.State(due).Should().Be(SelfBilledAcceptanceState.TacitlyAccepted);
        store.State(dueOnBoundary).Should().Be(SelfBilledAcceptanceState.TacitlyAccepted);
        store.State(noDeadline).Should().Be(SelfBilledAcceptanceState.PendingAcceptance,
            "sans échéance (mandat tacite / délai null), seule l'acceptation expresse débloque (BOFiP §290).");
        store.State(future).Should().Be(SelfBilledAcceptanceState.PendingAcceptance,
            "une échéance future ne bascule pas.");

        // Transition SYSTÈME : journal sans opérateur (operator_id null), libellé d'origine attendu.
        foreach (var key in new[] { due, dueOnBoundary })
        {
            var entry = store.SingleTransitionFor(key);
            entry.FromState.Should().Be(SelfBilledAcceptanceState.PendingAcceptance);
            entry.ToState.Should().Be(SelfBilledAcceptanceState.TacitlyAccepted);
            entry.OperatorId.Should().BeNull("une bascule tacite est une transition système, sans opérateur humain.");
            entry.OperatorName.Should().Be(TacitAcceptanceService.TacitOperatorName);
        }

        store.TransitionsFor(noDeadline).Should().BeEmpty();
        store.TransitionsFor(future).Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessDue_Skips_Acceptance_That_Became_Terminal_Before_The_Lock()
    {
        var store = new FakeAcceptanceStore();

        // Le lecteur a énuméré ce document comme dû, mais entre l'énumération et le verrou un opérateur l'a
        // accepté expressément (état terminal). Le service NE doit PAS tenter AcceptTacitly (machine fermée →
        // lèverait) : il re-vérifie sous verrou et passe.
        var alreadyAccepted = store.SeedTerminal(SelfBilledAcceptanceState.Accepted, deadline: Now.AddDays(-1));

        var service = new TacitAcceptanceService(store.Reader, store.UowFactory, new FixedTimeProvider(Now));
        var result = await service.ProcessDueAsync();

        result.TacitlyAccepted.Should().Be(0);
        store.State(alreadyAccepted).Should().Be(SelfBilledAcceptanceState.Accepted);
        store.TransitionsFor(alreadyAccepted).Should().BeEmpty("aucune transition n'a été écrite (anti-TOCTOU).");
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    /// <summary>
    /// Magasin d'acceptations en mémoire : sert de lecteur de candidats (renvoie TOUTES les clés semées,
    /// y compris non éligibles, pour éprouver la re-vérification du service) et d'unité de travail simulée
    /// (verrou = relecture du même agrégat ; transition = mutation in-place + journalisation enregistrée).
    /// </summary>
    private sealed class FakeAcceptanceStore
    {
        private readonly Dictionary<(Guid Company, Guid Document), SelfBilledAcceptance> _aggregates = [];
        private readonly List<SelfBilledAcceptanceLogEntry> _transitions = [];

        public ITacitAcceptanceCandidateReader Reader => new FakeReader(_aggregates.Keys.ToList());

        public ISelfBilledAcceptanceUnitOfWorkFactory UowFactory => new FakeUowFactory(_aggregates, _transitions);

        public TacitAcceptanceCandidate SeedPending(DateTimeOffset? deadline)
        {
            var companyId = Guid.NewGuid();
            var documentId = Guid.NewGuid();
            _aggregates[(companyId, documentId)] = SelfBilledAcceptance.Create(companyId, documentId, PendingSince, deadline);
            return new TacitAcceptanceCandidate(companyId, documentId);
        }

        public TacitAcceptanceCandidate SeedTerminal(SelfBilledAcceptanceState state, DateTimeOffset? deadline)
        {
            var companyId = Guid.NewGuid();
            var documentId = Guid.NewGuid();
            _aggregates[(companyId, documentId)] = SelfBilledAcceptance.Reconstitute(
                companyId, documentId, state, allocatedNumber: null, PendingSince, deadline, createdAt: PendingSince, updatedAt: PendingSince);
            return new TacitAcceptanceCandidate(companyId, documentId);
        }

        public SelfBilledAcceptanceState State(TacitAcceptanceCandidate key)
            => _aggregates[(key.CompanyId, key.DocumentId)].State;

        public List<SelfBilledAcceptanceLogEntry> TransitionsFor(TacitAcceptanceCandidate key)
            => _transitions.Where(e => e.CompanyId == key.CompanyId && e.DocumentId == key.DocumentId).ToList();

        public SelfBilledAcceptanceLogEntry SingleTransitionFor(TacitAcceptanceCandidate key)
            => TransitionsFor(key).Single();

        private sealed class FakeReader : ITacitAcceptanceCandidateReader
        {
            private readonly IReadOnlyList<(Guid Company, Guid Document)> _keys;

            public FakeReader(IReadOnlyList<(Guid Company, Guid Document)> keys) => _keys = keys;

            public Task<IReadOnlyList<TacitAcceptanceCandidate>> ListDueAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<TacitAcceptanceCandidate>>(
                    _keys.Select(k => new TacitAcceptanceCandidate(k.Company, k.Document)).ToList());
        }

        private sealed class FakeUowFactory : ISelfBilledAcceptanceUnitOfWorkFactory
        {
            private readonly Dictionary<(Guid Company, Guid Document), SelfBilledAcceptance> _aggregates;
            private readonly List<SelfBilledAcceptanceLogEntry> _transitions;

            public FakeUowFactory(Dictionary<(Guid Company, Guid Document), SelfBilledAcceptance> aggregates, List<SelfBilledAcceptanceLogEntry> transitions)
            {
                _aggregates = aggregates;
                _transitions = transitions;
            }

            public Task<ISelfBilledAcceptanceUnitOfWork> BeginAsync(CancellationToken ct = default)
                => Task.FromResult<ISelfBilledAcceptanceUnitOfWork>(new FakeUow(_aggregates, _transitions));
        }

        private sealed class FakeUow : ISelfBilledAcceptanceUnitOfWork
        {
            private readonly Dictionary<(Guid Company, Guid Document), SelfBilledAcceptance> _aggregates;
            private readonly List<SelfBilledAcceptanceLogEntry> _transitions;
            private SelfBilledAcceptanceLogEntry? _pending;

            public FakeUow(Dictionary<(Guid Company, Guid Document), SelfBilledAcceptance> aggregates, List<SelfBilledAcceptanceLogEntry> transitions)
            {
                _aggregates = aggregates;
                _transitions = transitions;
            }

            public Task InsertAsync(SelfBilledAcceptance acceptance, SelfBilledAcceptanceLogEntry logEntry, CancellationToken ct = default)
                => throw new NotSupportedException("Le service de bascule n'insère jamais d'acceptation.");

            public Task<SelfBilledAcceptance?> GetForUpdateAsync(Guid companyId, Guid documentId, CancellationToken ct = default)
                => Task.FromResult(_aggregates.GetValueOrDefault((companyId, documentId)));

            public Task SaveTransitionAsync(SelfBilledAcceptance acceptance, SelfBilledAcceptanceLogEntry logEntry, CancellationToken ct = default)
            {
                // L'agrégat est muté in-place (AcceptTacitly) ; on ne mémorise le journal qu'au commit.
                _pending = logEntry;
                return Task.CompletedTask;
            }

            public Task CommitAsync(CancellationToken ct = default)
            {
                if (_pending is not null)
                {
                    _transitions.Add(_pending);
                    _pending = null;
                }

                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
