namespace Liakont.Modules.Pipeline.Tests.Unit.B2cReporting;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Lifecycle;
using Liakont.Modules.Pipeline.Application;
using Liakont.Modules.Pipeline.Domain.B2cReporting;
using Liakont.Modules.Pipeline.Infrastructure.B2cReporting;
using Liakont.Modules.Transmission.Contracts;
using Liakont.PaClients.Fake;
using Microsoft.Extensions.Logging;
using Xunit;

/// <summary>
/// GDF02 — durcissement de la finalisation post-ACCEPTATION de <see cref="B2cReportingEmitter"/> (ADR-0037,
/// BUG-24). Prouve que, sur émission ACCEPTÉE, le gel de lien + la transition <c>EReported</c> sont résilients
/// PAR CONTRIBUTION : un échec (persistance) ou une annulation (shutdown) sur une contribution est journalisé
/// (7461) SANS faire retomber l'émission en <c>Technical</c>, SANS émettre le 7460 mensonger (« entrées Pending
/// conservées » — alors qu'elles sont Issued), et SANS interrompre les autres contributions. Ces défauts
/// laissaient un document déclaré figé <c>ReadyToSend</c> à vie (le rattrapage en régime permanent est couvert
/// en intégration : <c>B2cMarginAggregatorJobTests</c>).
/// </summary>
public sealed class B2cReportingEmitterTests
{
    private const int EmissionFailedEventId = 7460; // « entrées Pending conservées » — ne doit PAS mentir.
    private const int EReportFailedEventId = 7461;   // finalisation d'une contribution en échec (résiliente).

    [Fact]
    public async Task Freeze_Failure_On_One_Contribution_Does_Not_Abort_The_Others_Nor_Lie()
    {
        // Deux documents composent l'agrégat accepté ; le gel de lien ÉCHOUE pour le premier (erreur de
        // persistance) et réussit pour le second.
        var doc1 = Guid.NewGuid();
        var doc2 = Guid.NewGuid();
        var lifecycle = new RecordingEReportLifecycle();
        var pieceLinks = new FaultyReportingPieceLinkStore(failFor: doc1);
        var services = new MinimalServiceProvider()
            .With<IReportingPieceLinkStore>(pieceLinks)
            .With<IDocumentLifecycle>(lifecycle);
        var logger = new CapturingLogger();

        var tally = await B2cReportingEmitter.EmitAllAsync(
            services,
            new NoopEmissionStore(),
            new FakePaClient(),
            companyId: Guid.NewGuid(),
            transactions: new[] { TransactionWith(doc1, doc2) },
            category: EReportingTransactionCategory.Tma1,
            role: EReportingDeclarantRole.Seller,
            logger: logger,
            cancellationToken: CancellationToken.None);

        tally.Issued.Should().Be(1, "l'émission est ACCEPTÉE — un échec de finalisation ne la fait jamais retomber.");
        tally.Technical.Should().Be(0, "aucune émission acceptée ne doit être comptée en échec technique (mensonge inverse).");

        lifecycle.EReported.Should().Equal(new[] { doc2 },
            "le gel du 2e document réussit → il est transitionné EReported ; l'échec du 1er n'interrompt pas la boucle.");

        logger.EventIds.Should().Contain(EReportFailedEventId,
            "l'échec de finalisation de la contribution est signalé (7461) — jamais silencieux.");
        logger.EventIds.Should().NotContain(EmissionFailedEventId,
            "le 7460 (« entrées Pending conservées ») ne doit PAS mentir : les entrées sont Issued, pas Pending.");
    }

    [Fact]
    public async Task Cancellation_During_Finalization_Is_Logged_And_Never_Sinks_The_Accepted_Emission()
    {
        // Annulation (shutdown) pendant la finalisation d'une contribution ACCEPTÉE : elle est journalisée (7461)
        // et AVALÉE — l'émission reste Issued (jamais Technical), le résiduel est rattrapé au run suivant (D3).
        var doc = Guid.NewGuid();
        var lifecycle = new CancelingEReportLifecycle();
        var services = new MinimalServiceProvider()
            .With<IReportingPieceLinkStore>(new FaultyReportingPieceLinkStore(failFor: null))
            .With<IDocumentLifecycle>(lifecycle);
        var logger = new CapturingLogger();

        var tally = await B2cReportingEmitter.EmitAllAsync(
            services,
            new NoopEmissionStore(),
            new FakePaClient(),
            companyId: Guid.NewGuid(),
            transactions: new[] { TransactionWith(doc) },
            category: EReportingTransactionCategory.Tma1,
            role: EReportingDeclarantRole.Seller,
            logger: logger,
            cancellationToken: CancellationToken.None);

        tally.Issued.Should().Be(1, "l'émission acceptée reste Issued même si la transition est annulée (shutdown).");
        tally.Technical.Should().Be(0);
        logger.EventIds.Should().Contain(EReportFailedEventId, "l'annulation de la finalisation est journalisée (7461), jamais silencieuse.");
        logger.EventIds.Should().NotContain(EmissionFailedEventId);
    }

    private static B2cAggregatedTransaction TransactionWith(params Guid[] documentIds) => new()
    {
        Date = new DateOnly(2099, 3, 14),
        CurrencyCode = "EUR",
        TaxExclusiveAmount = 100m,
        TaxTotal = 20m,
        Subtotals = new[]
        {
            new B2cAggregatedSubtotal { RatePercent = 20m, TaxableAmount = 100m, TaxTotal = 20m },
        },
        Contributions = documentIds
            .Select(id => new B2cContributionRef { DocumentId = id, SourceReference = "src-" + id.ToString("N") })
            .ToList(),
    };

    /// <summary>Journal d'émission no-op : le test cible la finalisation post-acceptation, pas l'écriture du journal.</summary>
    private sealed class NoopEmissionStore : IB2cMarginEmissionStore
    {
        public Task<IReadOnlySet<Guid>> GetHandledDocumentIdsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());

        public Task AppendAsync(B2cMarginEmissionEntry entry, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    /// <summary>Gel de lien qui LÈVE pour un document donné (erreur de persistance simulée), réussit sinon.</summary>
    private sealed class FaultyReportingPieceLinkStore : IReportingPieceLinkStore
    {
        private readonly Guid? _failFor;

        public FaultyReportingPieceLinkStore(Guid? failFor) => _failFor = failFor;

        public Task<IReadOnlyList<ReportingPieceLink>> AppendAsync(Guid companyId, Guid documentId, IReadOnlyCollection<string> sourceReferences, CancellationToken cancellationToken = default)
        {
            if (_failFor is { } id && id == documentId)
            {
                throw new InvalidOperationException("Échec de persistance simulé du gel de lien.");
            }

            return Task.FromResult<IReadOnlyList<ReportingPieceLink>>(Array.Empty<ReportingPieceLink>());
        }

        public Task<IReadOnlyList<ReportingPieceLink>> GetByDocumentAsync(Guid companyId, Guid documentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ReportingPieceLink>> GetBySourceReferenceAsync(Guid companyId, string sourceReference, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>Cycle de vie qui ENREGISTRE les transitions EReported ; toute autre transition n'est pas attendue ici.</summary>
    private sealed class RecordingEReportLifecycle : StubLifecycle
    {
        public List<Guid> EReported { get; } = new();

        public override Task MarkEReportedAsync(Guid documentId, Guid emissionBatchId, CancellationToken cancellationToken = default)
        {
            EReported.Add(documentId);
            return Task.CompletedTask;
        }
    }

    /// <summary>Cycle de vie dont la transition EReported LÈVE une annulation (simule un shutdown en pleine finalisation).</summary>
    private sealed class CancelingEReportLifecycle : StubLifecycle
    {
        public override Task MarkEReportedAsync(Guid documentId, Guid emissionBatchId, CancellationToken cancellationToken = default) =>
            throw new OperationCanceledException();
    }

    /// <summary>Base de cycle de vie : tout membre non pertinent au test lève (jamais appelé par la voie testée).</summary>
    private abstract class StubLifecycle : IDocumentLifecycle
    {
        public virtual Task MarkEReportedAsync(Guid documentId, Guid emissionBatchId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task BlockAsync(Guid documentId, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task MarkReadyToSendAsync(Guid documentId, string mappingVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentRecheckPersistOutcome> MarkReadyToSendByRecheckAsync(Guid documentId, string mappingVersion, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentRecheckPersistOutcome> RecordRecheckStillBlockedAsync(Guid documentId, string reevaluatedReason, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentRecheckPersistOutcome> MarkBlockedByRecheckAsync(Guid documentId, string reevaluatedReason, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task BeginSendingAsync(Guid documentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RecordPaSendingReferenceAsync(Guid documentId, string paDocumentId, string? paResponseSnapshot, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task MarkIssuedAsync(Guid documentId, DocumentIssuanceSnapshots snapshots, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task MarkRejectedByPaAsync(Guid documentId, DocumentRejectionSnapshots snapshots, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task MarkTechnicalErrorAsync(Guid documentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentResolutionOutcome> ResolveManuallyAsync(Guid documentId, string reason, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentResolutionOutcome> SupersedeAsync(Guid documentId, Guid replacementDocumentId, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task ConfirmBuyerAsIndividualAsync(Guid documentId, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>Fournisseur de services minimal (le seul dont l'émetteur a besoin : gel de lien + cycle de vie).</summary>
    private sealed class MinimalServiceProvider : IServiceProvider
    {
        private readonly Dictionary<Type, object> _services = new();

        public MinimalServiceProvider With<TService>(TService instance)
            where TService : class
        {
            _services[typeof(TService)] = instance;
            return this;
        }

        public object? GetService(Type serviceType) =>
            _services.TryGetValue(serviceType, out var service) ? service : null;
    }

    /// <summary>Journal capturant les <see cref="EventId"/> émis (assertions 7460/7461).</summary>
    private sealed class CapturingLogger : ILogger
    {
        public List<int> EventIds { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NoopScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            EventIds.Add(eventId.Id);

        private sealed class NoopScope : IDisposable
        {
            public static readonly NoopScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
