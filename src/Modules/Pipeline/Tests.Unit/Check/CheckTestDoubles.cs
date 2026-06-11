namespace Liakont.Modules.Pipeline.Tests.Unit.Check;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Lifecycle;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Pipeline.Application;
using Liakont.Modules.Pipeline.Domain;
using Liakont.Modules.Staging.Contracts;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.TvaMapping.Contracts.Services;
using Liakont.Modules.Validation.Contracts;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Doubles de test (sans dépendance d'I/O ni de conteneur DI) pour le CHECK : un fournisseur de services
/// minimal (<see cref="IServiceProvider"/>) alimente le scope tenant que le consommateur résout. Chaque
/// faux n'implémente que ce que le CHECK appelle ; le reste lève <see cref="NotSupportedException"/>.
/// </summary>
internal static class CheckTestDoubles
{
    internal sealed class FakeServiceProvider : IServiceProvider
    {
        private readonly IReadOnlyDictionary<Type, object> _services;

        public FakeServiceProvider(IReadOnlyDictionary<Type, object> services) => _services = services;

        public object? GetService(Type serviceType) =>
            _services.TryGetValue(serviceType, out var service) ? service : null;
    }

    internal sealed class FakeTenantScopeFactory : ITenantScopeFactory
    {
        private readonly IServiceProvider _services;

        public FakeTenantScopeFactory(IServiceProvider services) => _services = services;

        public string? LastTenantId { get; private set; }

        public ITenantScope Create(string tenantId)
        {
            LastTenantId = tenantId;
            return new FakeTenantScope(tenantId, _services);
        }
    }

    internal sealed class FakeTenantScope : ITenantScope
    {
        public FakeTenantScope(string tenantId, IServiceProvider services)
        {
            TenantId = tenantId;
            Services = services;
        }

        public string TenantId { get; }

        public IServiceProvider Services { get; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    internal sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    internal sealed class FakeStagingStore : IPayloadStagingStore
    {
        private readonly string? _canonicalJson;
        private readonly Exception? _readException;

        private FakeStagingStore(string? canonicalJson, Exception? readException)
        {
            _canonicalJson = canonicalJson;
            _readException = readException;
        }

        public PayloadStagingStoreCapabilities Capabilities => throw new NotSupportedException();

        public static FakeStagingStore Returning(string canonicalJson) => new(canonicalJson, null);

        public static FakeStagingStore Throwing(Exception readException) => new(null, readException);

        public Task<string> ReadAsync(StagedPayloadKey key, CancellationToken cancellationToken = default)
        {
            if (_readException is not null)
            {
                throw _readException;
            }

            return Task.FromResult(_canonicalJson!);
        }

        public Task WriteAsync(StagedPayloadKey key, string canonicalJson, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> ExistsAsync(StagedPayloadKey key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task PurgeAsync(StagedPayloadKey key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    internal sealed class FakeTvaMappingService : ITvaMappingService
    {
        private readonly DocumentTvaMappingResult _result;

        public FakeTvaMappingService(DocumentTvaMappingResult result) => _result = result;

        public IReadOnlyList<TvaLineMappingRequest>? LastRequests { get; private set; }

        public Task<DocumentTvaMappingResult> MapAsync(
            Guid companyId,
            IReadOnlyList<TvaLineMappingRequest> lines,
            CancellationToken cancellationToken = default)
        {
            LastRequests = lines;
            return Task.FromResult(_result);
        }
    }

    internal sealed class FakeValidationService : IValidationService
    {
        private readonly ValidationResult _result;

        public FakeValidationService(ValidationResult result) => _result = result;

        public bool WasCalled { get; private set; }

        public Task<ValidationResult> ValidateAsync(DocumentValidationContext context, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(_result);
        }
    }

    internal sealed class FakeDocumentLifecycle : IDocumentLifecycle
    {
        public Guid? ReadyToSendId { get; private set; }

        public string? ReadyToSendMappingVersion { get; private set; }

        public Guid? BlockedId { get; private set; }

        public string? BlockReason { get; private set; }

        public Task BlockAsync(Guid documentId, string reason, CancellationToken cancellationToken = default)
        {
            BlockedId = documentId;
            BlockReason = reason;
            return Task.CompletedTask;
        }

        public Task MarkReadyToSendAsync(Guid documentId, string mappingVersion, CancellationToken cancellationToken = default)
        {
            ReadyToSendId = documentId;
            ReadyToSendMappingVersion = mappingVersion;
            return Task.CompletedTask;
        }

        public Task<DocumentRecheckPersistOutcome> MarkReadyToSendByRecheckAsync(Guid documentId, string mappingVersion, string operatorIdentity, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentRecheckPersistOutcome> RecordRecheckStillBlockedAsync(Guid documentId, string reevaluatedReason, string operatorIdentity, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task BeginSendingAsync(Guid documentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task MarkIssuedAsync(Guid documentId, DocumentIssuanceSnapshots snapshots, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task MarkRejectedByPaAsync(Guid documentId, DocumentRejectionSnapshots snapshots, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task MarkTechnicalErrorAsync(Guid documentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentResolutionOutcome> ResolveManuallyAsync(Guid documentId, string reason, string operatorIdentity, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentResolutionOutcome> SupersedeAsync(Guid documentId, Guid replacementDocumentId, string operatorIdentity, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ConfirmBuyerAsIndividualAsync(Guid documentId, string operatorIdentity, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    internal sealed class FakeDocumentQueries : IDocumentQueries
    {
        private readonly DocumentDto? _document;

        public FakeDocumentQueries(DocumentDto? document) => _document = document;

        public Task<DocumentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_document);

        public Task<DocumentDto?> GetByNumberAsync(string documentNumber, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentSummaryDto>> GetByStateAsync(string state, int page, int pageSize, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentEventDto>> GetEventsAsync(Guid documentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentSummaryDto>> GetPotentiallySentDocumentsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentStatusDto?> FindStatusBySourceReferenceAndPayloadHashAsync(string sourceReference, string payloadHash, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentListResult> GetDocumentsAsync(DocumentListFilter filter, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ArchiveReferenceDto?> GetArchiveReferenceAsync(Guid documentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentSummaryDto?> GetOldestDocumentInStateAsync(string state, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    internal sealed class FakeTenantSettingsQueries : ITenantSettingsQueries
    {
        private readonly Guid? _companyId;
        private readonly IReadOnlyList<PaAccountDto> _paAccounts;

        public FakeTenantSettingsQueries(Guid? companyId, IReadOnlyList<PaAccountDto>? paAccounts = null)
        {
            _companyId = companyId;
            _paAccounts = paAccounts ?? Array.Empty<PaAccountDto>();
        }

        public Task<Guid?> GetCurrentCompanyId(CancellationToken ct = default) => Task.FromResult(_companyId);

        public Task<bool> GetAuctionVerticalEnabled(Guid companyId, CancellationToken ct = default) => Task.FromResult(false);

        public Task<IReadOnlyList<PaAccountDto>> GetPaAccounts(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult(_paAccounts);

        public Task<TenantProfileDto?> GetTenantProfile(Guid companyId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<FiscalSettingsDto?> GetFiscalSettings(Guid companyId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ExtractionScheduleDto?> GetExtractionSchedule(Guid companyId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<AlertThresholdsDto?> GetAlertThresholds(Guid companyId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    internal sealed class FakeRunLogStore : IPipelineRunLogStore
    {
        public RunLog? Saved { get; private set; }

        public Task SaveAsync(RunLog runLog, CancellationToken cancellationToken = default)
        {
            Saved = runLog;
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeVentilationSnapshotStore : IVentilationSnapshotStore
    {
        public Liakont.Modules.Pipeline.Domain.Ventilation.VentilationSnapshot? Saved { get; private set; }

        public Task<bool> SaveAsync(Liakont.Modules.Pipeline.Domain.Ventilation.VentilationSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            Saved = snapshot;
            return Task.FromResult(true);
        }

        public Task<Liakont.Modules.Pipeline.Domain.Ventilation.VentilationSnapshot?> GetAsync(Guid documentId, string mappingVersion, CancellationToken cancellationToken = default) =>
            Task.FromResult(Saved);
    }
}
