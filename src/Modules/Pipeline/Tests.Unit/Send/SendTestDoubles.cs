namespace Liakont.Modules.Pipeline.Tests.Unit.Send;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Lifecycle;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Pipeline.Application;
using Liakont.Modules.Pipeline.Domain;
using Liakont.Modules.Staging.Contracts;
using Liakont.Modules.SupportTrace.Contracts;
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

        public Task<DocumentRecheckPersistOutcome> MarkReadyToSendByRecheckAsync(Guid documentId, string mappingVersion, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentRecheckPersistOutcome> RecordRecheckStillBlockedAsync(Guid documentId, string reevaluatedReason, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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

        public Task<DocumentResolutionOutcome> ResolveManuallyAsync(Guid documentId, string reason, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentResolutionOutcome> SupersedeAsync(Guid documentId, Guid replacementDocumentId, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ConfirmBuyerAsIndividualAsync(Guid documentId, string operatorIdentity, string? operatorName, CancellationToken cancellationToken = default) =>
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

        /// <summary>Statut du tenant courant : null = pas de profil = ACTIF (defaut neutre des tests).</summary>
        public Task<string?> GetCurrentTenantStatut(CancellationToken ct = default) =>
            Task.FromResult<string?>(null);

        public Task<bool> GetAuctionVerticalEnabled(Guid companyId, CancellationToken ct = default) => Task.FromResult(false);

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

    /// <summary>
    /// PA de test de niveau « Essentiel » (FX07) : déclare <c>SupportsFacturXTransmission</c> et ne fait que
    /// TRANSMETTRE l'artefact pré-construit porté par le <see cref="PaSendContext"/> — bloque si absent (jamais
    /// d'envoi à vide). Enregistre les octets reçus pour prouver que le pipeline génère et passe le Factur-X.
    /// </summary>
    internal sealed class FacturXCapablePaClient : IPaClient
    {
        public PaCapabilities Capabilities { get; } = new() { PaName = "Test générique", SupportsFacturXTransmission = true };

        /// <summary>Octets EXACTS reçus dans le contexte d'envoi (preuve « généré avant transmission, passé tel quel »).</summary>
        public byte[]? ReceivedArtifact { get; private set; }

        public int SendCount { get; private set; }

        public Task<PaSendResult> SendDocumentAsync(PivotDocumentDto document, bool sendAfterImport = true, PaSendContext? context = null, CancellationToken cancellationToken = default)
        {
            SendCount++;
            var artifact = context?.PreBuiltArtifact ?? default;
            if (artifact.IsEmpty)
            {
                // Garde-fou identique au vrai plug-in générique : jamais d'émission « à vide ».
                return Task.FromResult(PaSendResult.Technical([new PaError("FXG_ARTEFACT_REQUIS", "Artefact requis.")]));
            }

            ReceivedArtifact = artifact.ToArray();
            return Task.FromResult(PaSendResult.Issued($"GENERIQUE-{document.Number}", rawResponse: "{\"status\":\"accepted\"}"));
        }

        public Task<PaSendResult> SendPaymentReportAsync(PaymentReportPeriod period, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PaDocumentStatus> GetDocumentStatusAsync(string paDocumentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PaTaxReport>> ListTaxReportsAsync(DateTime? since = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PaTaxReport> GetTaxReportAsync(string taxReportId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PaAccountInfo> GetAccountInfoAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        // Niveau Essentiel : aucun tax_report_setting. Le SEND saute ce diagnostic pour une PA à capacité
        // SupportsFacturXTransmission, mais on rend un DTO neutre par robustesse (jamais d'exception ici).
        public Task<PaTaxReportSetting> GetTaxReportSettingAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PaTaxReportSetting());

        public Task EnsureTaxReportSettingAsync(PaTaxReportSettingRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PaGeneratedDocument> GetGeneratedDocumentAsync(string paDocumentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>Pont de génération de test (FX07) : renvoie un artefact canonique fixe et compte ses appels.</summary>
    internal sealed class StubFacturXArtifactBuilder : IFacturXArtifactBuilder
    {
        private readonly byte[] _artifact;

        public StubFacturXArtifactBuilder(byte[] artifact) => _artifact = artifact;

        public int BuildCount { get; private set; }

        public Task<ReadOnlyMemory<byte>> BuildSealedArtifactAsync(PivotDocumentDto pivot, CancellationToken cancellationToken = default)
        {
            BuildCount++;
            return Task.FromResult<ReadOnlyMemory<byte>>(_artifact);
        }
    }

    /// <summary>Recherche d'envoi PA journalisé de test (FX07) : configurable par clé d'idempotence (défaut : rien).</summary>
    internal sealed class StubPaTransmissionJournalQueries : Liakont.Modules.Documents.Contracts.Queries.IPaTransmissionJournalQueries
    {
        private readonly Dictionary<string, PaTransmissionJournalDto> _byKey = new(StringComparer.Ordinal);

        /// <summary>Déclare qu'un envoi est DÉJÀ journalisé pour cette clé (numéro de document) — déclenche la garde.</summary>
        public void AddJournaled(string idempotencyKey, Guid documentId)
        {
            _byKey[idempotencyKey] = new PaTransmissionJournalDto
            {
                EventId = Guid.NewGuid(),
                DocumentId = documentId,
                TimestampUtc = SendTestData.Now,
                IdempotencyKey = idempotencyKey,
                PaAccount = "compte-generique",
                PaPluginId = "generique",
                PaRequestUtc = SendTestData.Now,
                PaResponseUtc = SendTestData.Now,
                TransmittedArtifactHash = "sha256:deadbeef",
            };
        }

        public Task<PaTransmissionJournalDto?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(_byKey.TryGetValue(idempotencyKey, out var dto) ? dto : null);
    }

    /// <summary>Journal d'envoi PA de test (FX07) : enregistre chaque entrée journalisée par le pipeline.</summary>
    internal sealed class RecordingPaTransmissionJournal : IPaTransmissionJournal
    {
        public List<PaTransmissionJournalEntry> Entries { get; } = new();

        public Task JournalAsync(PaTransmissionJournalEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    /// <summary>Trace de support de test (FX07) : enregistre les écritures (tenant, document, octets).</summary>
    internal sealed class RecordingSupportTraceStore : ISupportTraceStore
    {
        public List<(string TenantId, Guid DocumentId, byte[] FacturX)> Writes { get; } = new();

        public Task WriteAsync(string tenantId, Guid documentId, ReadOnlyMemory<byte> facturX, DateTimeOffset recordedAtUtc, CancellationToken cancellationToken = default)
        {
            Writes.Add((tenantId, documentId, facturX.ToArray()));
            return Task.CompletedTask;
        }

        public Task<byte[]?> ReadAsync(string tenantId, Guid documentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> PurgeOlderThanAsync(string tenantId, DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
