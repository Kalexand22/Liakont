namespace Liakont.Modules.Pipeline.Tests.Unit.Send;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Lifecycle;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Pipeline.Application;
using Liakont.Modules.Pipeline.Domain;
using Liakont.Modules.Staging.Contracts;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Doubles de test (sans I/O ni conteneur DI) pour le SEND : un <see cref="IServiceProvider"/> minimal
/// alimente le scope tenant que le job résout. Chaque faux n'implémente que ce que le SEND appelle ;
/// le reste lève <see cref="NotSupportedException"/>.
/// </summary>
internal static class SendTestDoubles
{
    internal sealed class FakeServiceProvider : IServiceProvider
    {
        private readonly Dictionary<Type, object> _services = new();

        public FakeServiceProvider Add<TService>(TService instance)
            where TService : class
        {
            _services[typeof(TService)] = instance;
            return this;
        }

        public object? GetService(Type serviceType) =>
            _services.TryGetValue(serviceType, out var service) ? service : null;
    }

    internal sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    internal sealed class StubPaClientRegistry : IPaClientRegistry
    {
        private readonly IPaClient _client;

        public StubPaClientRegistry(IPaClient client) => _client = client;

        public IReadOnlyCollection<string> RegisteredTypes => Array.Empty<string>();

        public IPaClient Resolve(PaAccountDescriptor account) => _client;

        public bool IsRegistered(string paType) => true;
    }

    internal sealed class ConfigurableDocumentQueries : IDocumentQueries
    {
        private readonly Dictionary<Guid, DocumentDto> _byId = new();
        private readonly Dictionary<string, List<DocumentSummaryDto>> _byState = new(StringComparer.Ordinal);
        private readonly List<DocumentSummaryDto> _potentiallySent = new();

        public void AddDocument(DocumentDto document) => _byId[document.Id] = document;

        public void AddInState(string state, DocumentSummaryDto summary)
        {
            if (!_byState.TryGetValue(state, out var list))
            {
                list = new List<DocumentSummaryDto>();
                _byState[state] = list;
            }

            list.Add(summary);
        }

        public void AddPotentiallySent(DocumentSummaryDto summary) => _potentiallySent.Add(summary);

        public Task<DocumentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_byId.TryGetValue(id, out var document) ? document : null);

        public Task<IReadOnlyList<DocumentSummaryDto>> GetByStateAsync(string state, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<DocumentSummaryDto> result =
                _byState.TryGetValue(state, out var list)
                    ? list.Skip((page - 1) * pageSize).Take(pageSize).ToList()
                    : Array.Empty<DocumentSummaryDto>();
            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<DocumentSummaryDto>> GetPotentiallySentDocumentsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DocumentSummaryDto>>(_potentiallySent);

        public Task<DocumentDto?> GetByNumberAsync(string documentNumber, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentEventDto>> GetEventsAsync(Guid documentId, CancellationToken cancellationToken = default) =>
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

    internal sealed class RecordingDocumentLifecycle : IDocumentLifecycle
    {
        public List<Guid> Blocked { get; } = new();

        public List<Guid> ReadyToSend { get; } = new();

        public List<Guid> BeganSending { get; } = new();

        public List<Guid> Issued { get; } = new();

        public List<Guid> Rejected { get; } = new();

        public List<Guid> TechnicalError { get; } = new();

        public Task BlockAsync(Guid documentId, string reason, CancellationToken cancellationToken = default)
        {
            Blocked.Add(documentId);
            return Task.CompletedTask;
        }

        public Task MarkReadyToSendAsync(Guid documentId, string mappingVersion, CancellationToken cancellationToken = default)
        {
            ReadyToSend.Add(documentId);
            return Task.CompletedTask;
        }

        public Task BeginSendingAsync(Guid documentId, CancellationToken cancellationToken = default)
        {
            BeganSending.Add(documentId);
            return Task.CompletedTask;
        }

        public Task MarkIssuedAsync(Guid documentId, DocumentIssuanceSnapshots snapshots, CancellationToken cancellationToken = default)
        {
            Issued.Add(documentId);
            return Task.CompletedTask;
        }

        public Task MarkRejectedByPaAsync(Guid documentId, DocumentRejectionSnapshots snapshots, CancellationToken cancellationToken = default)
        {
            Rejected.Add(documentId);
            return Task.CompletedTask;
        }

        public Task MarkTechnicalErrorAsync(Guid documentId, CancellationToken cancellationToken = default)
        {
            TechnicalError.Add(documentId);
            return Task.CompletedTask;
        }

        public Task<DocumentResolutionOutcome> ResolveManuallyAsync(Guid documentId, string reason, string operatorIdentity, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentResolutionOutcome> SupersedeAsync(Guid documentId, Guid replacementDocumentId, string operatorIdentity, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    internal sealed class MapStagingStore : IPayloadStagingStore
    {
        private readonly Dictionary<Guid, string> _byDocument = new();
        private readonly HashSet<Guid> _integrity = new();

        public PayloadStagingStoreCapabilities Capabilities => new(false, false);

        public void Stage(Guid documentId, string canonicalJson) => _byDocument[documentId] = canonicalJson;

        public void StageIntegrityFailure(Guid documentId) => _integrity.Add(documentId);

        public Task<string> ReadAsync(StagedPayloadKey key, CancellationToken cancellationToken = default)
        {
            if (_integrity.Contains(key.DocumentId))
            {
                throw StagedPayloadIntegrityException.HashMismatch(key, "altered");
            }

            if (_byDocument.TryGetValue(key.DocumentId, out var json))
            {
                return Task.FromResult(json);
            }

            throw StagedPayloadNotFoundException.ForKey(key);
        }

        public Task WriteAsync(StagedPayloadKey key, string canonicalJson, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> ExistsAsync(StagedPayloadKey key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_byDocument.ContainsKey(key.DocumentId));

        public Task PurgeAsync(StagedPayloadKey key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    internal sealed class RecordingStagingPurgeService : IStagingPurgeService
    {
        private readonly bool _wormPresent;

        public RecordingStagingPurgeService(bool wormPresent) => _wormPresent = wormPresent;

        public List<StagedPayloadKey> Calls { get; } = new();

        public Task<bool> PurgeIfArchivedAsync(StagedPayloadKey key, ArchivedDocumentLocator locator, CancellationToken cancellationToken = default)
        {
            Calls.Add(key);
            return Task.FromResult(_wormPresent);
        }
    }

    internal sealed class RecordingArchiveService : IArchiveService
    {
        public List<ArchivePackageRequest> Requests { get; } = new();

        public Task<ArchivePackageResult> ArchiveIssuedDocumentAsync(ArchivePackageRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ArchivePackageResult(
                Guid.NewGuid(), request.DocumentId, "2026/01/" + request.DocumentNumber + "/manifest.json", "hash", "chain", SendTestData.Now));
        }

        public Task<ArchivePackageResult> AddAddendumAsync(ArchiveAddendumRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ArchiveIntegrityReport> VerifyTenantChainAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    internal sealed class ConfigurableTenantSettingsQueries : ITenantSettingsQueries
    {
        private readonly Guid? _companyId;
        private readonly IReadOnlyList<PaAccountDto> _accounts;

        public ConfigurableTenantSettingsQueries(Guid? companyId, IReadOnlyList<PaAccountDto>? accounts = null)
        {
            _companyId = companyId;
            _accounts = accounts ?? Array.Empty<PaAccountDto>();
        }

        public Task<Guid?> GetCurrentCompanyId(CancellationToken ct = default) => Task.FromResult(_companyId);

        public Task<IReadOnlyList<PaAccountDto>> GetPaAccounts(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult(_accounts);

        public Task<TenantProfileDto?> GetTenantProfile(Guid companyId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<FiscalSettingsDto?> GetFiscalSettings(Guid companyId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ExtractionScheduleDto?> GetExtractionSchedule(Guid companyId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<AlertThresholdsDto?> GetAlertThresholds(Guid companyId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    internal sealed class RecordingRunLogStore : IPipelineRunLogStore
    {
        public List<RunLog> Saved { get; } = new();

        public Task SaveAsync(RunLog runLog, CancellationToken cancellationToken = default)
        {
            Saved.Add(runLog);
            return Task.CompletedTask;
        }
    }
}
