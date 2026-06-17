namespace Liakont.Host.Tests.Unit.Signatures;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Signatures;
using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.DocumentApproval.Contracts.DTOs;
using Liakont.Modules.DocumentApproval.Contracts.Queries;
using Liakont.Modules.Signature.Contracts;
using Stratum.Common.Abstractions.Security;
using Xunit;

/// <summary>
/// Tests unitaires du service de lecture de la page console des signatures (SIG10). Vérifie la résolution
/// tenant (<c>company_id</c> obligatoire), la délégation aux ports <see cref="IDocumentApprovalQueries"/>
/// et <see cref="ISignatureProviderRegistry"/>, et la composition du <see cref="SignatureStatusView"/> —
/// sans toucher à une base ni à un module réel.
/// </summary>
public sealed class SignatureConsoleQueryServiceTests
{
    private static readonly Guid CompanyId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DocId = new("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task GetStatusAsync_When_CompanyId_Is_Null_Throws_InvalidOperationException()
    {
        var service = Build(companyId: null, new FakeApprovalQueries(), new FakeProviderRegistry());

        var act = async () => await service.GetStatusAsync(DocId, ValidationPurpose.SelfBilledAcceptance);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Tenant non résolu*");
    }

    [Fact]
    public async Task GetStatusAsync_With_Resolved_Tenant_Propagates_CompanyId_And_Composes_View()
    {
        var latest = new DocumentValidationDto
        {
            DocumentId = DocId,
            Purpose = ValidationPurpose.SelfBilledAcceptance,
            Attempt = 1,
            State = "PendingValidation",
            ProofLevel = "None",
            ExpressAcceptanceRecorded = false,
            IsTerminal = false,
        };
        var logEntry = new DocumentApprovalLogEntryDto
        {
            DocumentId = DocId,
            Purpose = ValidationPurpose.SelfBilledAcceptance,
            Attempt = 1,
            FromState = null,
            ToState = "PendingValidation",
            OccurredAt = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
        };
        var approvals = new FakeApprovalQueries { LatestResult = latest, LogResult = [logEntry] };
        var service = Build(CompanyId, approvals, new FakeProviderRegistry());

        var view = await service.GetStatusAsync(DocId, ValidationPurpose.SelfBilledAcceptance);

        // Both queries receive the exact companyId and documentId.
        approvals.LatestCall.Should().Be((CompanyId, DocId, ValidationPurpose.SelfBilledAcceptance));
        approvals.LogCall.Should().Be((CompanyId, DocId, ValidationPurpose.SelfBilledAcceptance));

        // The view is composed from both query results.
        view.Latest.Should().BeSameAs(latest);
        view.Log.Should().ContainSingle().Which.Should().BeSameAs(logEntry);
    }

    [Fact]
    public void GetConfiguredProviderTypes_Returns_Registry_RegisteredTypes_When_Not_Empty()
    {
        var registry = new FakeProviderRegistry { RegisteredTypesValue = ["Yousign"] };
        var service = Build(CompanyId, new FakeApprovalQueries(), registry);

        var result = service.GetConfiguredProviderTypes();

        result.Should().ContainSingle().Which.Should().Be("Yousign");
    }

    [Fact]
    public void GetConfiguredProviderTypes_Returns_Empty_Collection_When_Registry_Is_Empty()
    {
        var registry = new FakeProviderRegistry { RegisteredTypesValue = [] };
        var service = Build(CompanyId, new FakeApprovalQueries(), registry);

        var result = service.GetConfiguredProviderTypes();

        result.Should().BeEmpty();
    }

    private static SignatureConsoleQueryService Build(Guid? companyId, IDocumentApprovalQueries approvals, ISignatureProviderRegistry providers)
    {
        var actor = new FakeActorContextAccessor(companyId);
        return new SignatureConsoleQueryService(approvals, providers, actor);
    }

    private sealed class FakeApprovalQueries : IDocumentApprovalQueries
    {
        public (Guid CompanyId, Guid DocumentId, ValidationPurpose Purpose)? LatestCall { get; private set; }

        public (Guid CompanyId, Guid DocumentId, ValidationPurpose Purpose)? LogCall { get; private set; }

        public DocumentValidationDto? LatestResult { get; set; }

        public IReadOnlyList<DocumentApprovalLogEntryDto> LogResult { get; set; } = [];

        public Task<DocumentValidationDto?> GetLatestAttempt(Guid companyId, Guid documentId, ValidationPurpose purpose, CancellationToken ct = default)
        {
            LatestCall = (companyId, documentId, purpose);
            return Task.FromResult(LatestResult);
        }

        public Task<IReadOnlyList<DocumentApprovalLogEntryDto>> GetApprovalLog(Guid companyId, Guid documentId, ValidationPurpose purpose, CancellationToken ct = default)
        {
            LogCall = (companyId, documentId, purpose);
            return Task.FromResult(LogResult);
        }

        public Task<IReadOnlyList<TacitDueDocumentDto>> ListTacitDueDocumentsAsync(ValidationPurpose purpose, DateTimeOffset nowUtc, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TacitDueDocumentDto>>([]);
    }

    private sealed class FakeProviderRegistry : ISignatureProviderRegistry
    {
        public IReadOnlyCollection<string> RegisteredTypesValue { get; set; } = [];

        public IReadOnlyCollection<string> RegisteredTypes => RegisteredTypesValue;

        public ISignatureProvider Resolve(SignatureProviderAccount account) =>
            throw new NotImplementedException();

        public bool IsRegistered(string providerType) => false;
    }

    private sealed class FakeActorContextAccessor : IActorContextAccessor
    {
        public FakeActorContextAccessor(Guid? companyId) =>
            Current = new FakeActorContext(companyId);

        public IActorContext Current { get; }

        private sealed class FakeActorContext : IActorContext
        {
            public FakeActorContext(Guid? companyId) => CompanyId = companyId;

            public Guid UserId => Guid.Empty;

            public Guid CorrelationId => Guid.Empty;

            public bool IsAuthenticated => true;

            public string? DisplayName => null;

            public string? Email => null;

            public Guid? CompanyId { get; }

            public string? Timezone => null;

            public string? Language => "fr";

            public string? TenantId => "tenant-test";
        }
    }
}
