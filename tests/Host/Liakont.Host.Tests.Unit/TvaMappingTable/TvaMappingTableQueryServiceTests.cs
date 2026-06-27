namespace Liakont.Host.Tests.Unit.TvaMappingTable;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.TvaMappingTable;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.TvaMapping.Contracts.Commands;
using Liakont.Modules.TvaMapping.Contracts.DTOs;
using Liakont.Modules.TvaMapping.Contracts.Queries;
using MediatR;
using Stratum.Common.Abstractions.Security;
using Xunit;

/// <summary>
/// Tests unitaires de <see cref="TvaMappingTableQueryService"/> (WEB07a) : résolution de la société par
/// l'identité authentifiée (même source que la commande de validation), dégradation en vue vide quand
/// aucune société n'est résolue (jamais une erreur, jamais une lecture cross-tenant), et validation
/// déléguée à la commande TVA05 avec le valideur = identité authentifiée (jamais une valeur de l'UI).
/// </summary>
public sealed class TvaMappingTableQueryServiceTests
{
    [Fact]
    public async Task GetTableAsync_With_No_Resolved_Company_Returns_Empty_And_Does_Not_Read()
    {
        var queries = new FakeTvaMappingQueries(table: SomeTable());
        var service = new TvaMappingTableQueryService(queries, Actor(companyId: null), new CapturingSender(), new FakeTenantSettingsQueries());

        var model = await service.GetTableAsync();

        model.Table.Should().BeNull("sans société résolue, aucune table n'est lue (vue vide transitoire)");
        model.ChangeLog.Should().BeEmpty();
        queries.GetMappingTableCalls.Should().Be(0, "aucune lecture cross-tenant n'est tentée sans société");
    }

    [Fact]
    public async Task GetTableAsync_With_Resolved_Company_Passes_It_Through_And_Returns_Table_And_Log()
    {
        var companyId = Guid.NewGuid();
        var table = SomeTable();
        var log = new[] { SomeLogEntry() };
        var queries = new FakeTvaMappingQueries(table, log);
        var service = new TvaMappingTableQueryService(queries, Actor(companyId: companyId), new CapturingSender(), new FakeTenantSettingsQueries());

        var model = await service.GetTableAsync();

        queries.LastCompanyId.Should().Be(companyId, "la société du contexte est passée aux lectures tenant-scopées");
        model.Table.Should().BeSameAs(table);
        model.ChangeLog.Should().BeEquivalentTo(log);
    }

    [Theory]
    [InlineData("Alice Martin", "alice@x.fr", "Alice Martin")]
    [InlineData(null, "alice@x.fr", "alice@x.fr")]
    public async Task GetTableAsync_Resolves_Operator_Identity_With_Fallback(string? displayName, string? email, string expected)
    {
        var service = new TvaMappingTableQueryService(
            new FakeTvaMappingQueries(table: null),
            Actor(companyId: null, displayName: displayName, email: email),
            new CapturingSender(),
            new FakeTenantSettingsQueries());

        var model = await service.GetTableAsync();

        model.CurrentOperatorName.Should().Be(expected);
    }

    [Fact]
    public async Task GetTableAsync_Falls_Back_To_UserId_When_No_Name_Or_Email()
    {
        var userId = Guid.NewGuid();
        var service = new TvaMappingTableQueryService(
            new FakeTvaMappingQueries(table: null),
            Actor(companyId: null, displayName: null, email: null, userId: userId),
            new CapturingSender(),
            new FakeTenantSettingsQueries());

        var model = await service.GetTableAsync();

        model.CurrentOperatorName.Should().Be(userId.ToString());
    }

    [Fact]
    public async Task ValidateAsync_Sends_Validate_Command_With_Authenticated_Operator_Identity()
    {
        var sender = new CapturingSender();
        var service = new TvaMappingTableQueryService(
            new FakeTvaMappingQueries(table: SomeTable()),
            Actor(companyId: Guid.NewGuid(), displayName: "Alice Martin"),
            sender,
            new FakeTenantSettingsQueries());

        await service.ValidateAsync();

        sender.Captured.Should().BeOfType<ValidateMappingTableCommand>()
            .Which.ValidatedBy.Should().Be("Alice Martin", "le valideur est l'identité authentifiée, jamais une valeur de l'UI");
    }

    [Fact]
    public async Task GetTableAsync_Reads_AuctionVerticalActivation_And_Consistency_For_Resolved_Company()
    {
        var companyId = Guid.NewGuid();
        var settingsQueries = new FakeTenantSettingsQueries { AuctionVerticalEnabled = true };
        var service = new TvaMappingTableQueryService(
            new FakeTvaMappingQueries(table: SomeTable()),
            Actor(companyId: companyId),
            new CapturingSender(),
            settingsQueries);

        var model = await service.GetTableAsync();

        settingsQueries.CapturedCompanyId.Should().Be(companyId, "l'activation est lue pour la société du contexte (tenant-scopé)");
        model.TenantResolved.Should().BeTrue();
        model.AuctionVerticalEnabled.Should().BeTrue();
        model.Consistency.Should().NotBeNull("le rapport de cohérence est calculé pour un tenant résolu");
    }

    [Fact]
    public async Task GetTableAsync_Unresolved_Company_Does_Not_Read_Activation()
    {
        var settingsQueries = new FakeTenantSettingsQueries { AuctionVerticalEnabled = true };
        var service = new TvaMappingTableQueryService(
            new FakeTvaMappingQueries(table: null),
            Actor(companyId: null),
            new CapturingSender(),
            settingsQueries);

        var model = await service.GetTableAsync();

        model.TenantResolved.Should().BeFalse();
        model.AuctionVerticalEnabled.Should().BeFalse("défaut OFF en vue vide");
        model.Consistency.Should().BeNull();
        settingsQueries.CapturedCompanyId.Should().BeNull("aucune lecture tenant sans société résolue");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetAuctionVerticalAsync_Sends_Command_With_Requested_State(bool enabled)
    {
        var sender = new CapturingSender();
        var service = new TvaMappingTableQueryService(
            new FakeTvaMappingQueries(table: SomeTable()),
            Actor(companyId: Guid.NewGuid()),
            sender,
            new FakeTenantSettingsQueries());

        await service.SetAuctionVerticalAsync(enabled);

        sender.Captured.Should().BeOfType<SetAuctionVerticalActivationCommand>()
            .Which.Enabled.Should().Be(enabled);
    }

    private static MappingTableDto SomeTable() => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Guid.NewGuid(),
        MappingVersion = "v1",
        IsValidated = false,
        DefaultBehavior = "Block",
        CreatedAt = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
        Rules = Array.Empty<MappingRuleDto>(),
    };

    private static MappingChangeLogEntryDto SomeLogEntry() => new()
    {
        Id = Guid.NewGuid(),
        ChangeType = "Validate",
        MappingVersion = "v1",
        OperatorId = Guid.NewGuid(),
        OperatorName = "Alice Martin",
        OccurredAt = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
    };

    private static FakeActorContextAccessor Actor(
        Guid? companyId,
        string? displayName = "Alice Martin",
        string? email = null,
        Guid? userId = null) =>
        new(companyId, displayName, email, userId ?? Guid.NewGuid());

    private sealed class FakeTvaMappingQueries : ITvaMappingQueries
    {
        private readonly MappingTableDto? _table;
        private readonly IReadOnlyList<MappingChangeLogEntryDto> _changeLog;

        public FakeTvaMappingQueries(MappingTableDto? table, IReadOnlyList<MappingChangeLogEntryDto>? changeLog = null)
        {
            _table = table;
            _changeLog = changeLog ?? Array.Empty<MappingChangeLogEntryDto>();
        }

        public Guid? LastCompanyId { get; private set; }

        public int GetMappingTableCalls { get; private set; }

        public Task<MappingTableDto?> GetMappingTable(Guid companyId, CancellationToken ct = default)
        {
            GetMappingTableCalls++;
            LastCompanyId = companyId;
            return Task.FromResult(_table);
        }

        public Task<IReadOnlyList<MappingChangeLogEntryDto>> GetChangeLog(Guid companyId, CancellationToken ct = default)
        {
            LastCompanyId = companyId;
            return Task.FromResult(_changeLog);
        }
    }

    private sealed class FakeActorContextAccessor : IActorContextAccessor
    {
        public FakeActorContextAccessor(Guid? companyId, string? displayName, string? email, Guid userId) =>
            Current = new FakeActorContext(companyId, displayName, email, userId);

        public IActorContext Current { get; }

        private sealed class FakeActorContext : IActorContext
        {
            public FakeActorContext(Guid? companyId, string? displayName, string? email, Guid userId)
            {
                CompanyId = companyId;
                DisplayName = displayName;
                Email = email;
                UserId = userId;
            }

            public Guid UserId { get; }

            public Guid CorrelationId { get; } = Guid.NewGuid();

            public bool IsAuthenticated => true;

            public string? DisplayName { get; }

            public string? Email { get; }

            public Guid? CompanyId { get; }

            public string? Timezone => null;

            public string? Language => "fr";

            public string? TenantId => "tenant-test";
        }
    }

    private sealed class CapturingSender : ISender
    {
        public IRequest? Captured { get; private set; }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            Captured = request;
            return Task.CompletedTask;
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            // Les lectures de la page (options d'édition + couverture) passent par cette surcharge typée.
            object response = request switch
            {
                GetTvaMappingEditOptionsQuery => new TvaMappingEditOptionsDto
                {
                    Categories = Array.Empty<TvaMappingOptionDto>(),
                    Parts = Array.Empty<TvaMappingOptionDto>(),
                    RateModes = Array.Empty<TvaMappingOptionDto>(),
                    VatexCodes = Array.Empty<TvaMappingOptionDto>(),
                },
                GetMappingCoverageReportQuery => new MappingCoverageReportDto
                {
                    IsTableConfigured = false,
                    IsTableValidated = false,
                    Verdict = "Incomplete",
                    CoveredRegimes = Array.Empty<RegimeCoverageDto>(),
                    AbsentRegimes = Array.Empty<RegimeCoverageDto>(),
                },
                GetMappingConsistencyReportQuery => new MappingConsistencyReportDto
                {
                    IsTableConfigured = false,
                    DeadRules = Array.Empty<DeadMappingRuleDto>(),
                },
                _ => throw new NotSupportedException(),
            };

            return Task.FromResult((TResponse)response);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeTenantSettingsQueries : ITenantSettingsQueries
    {
        public bool AuctionVerticalEnabled { get; init; }

        public Guid? CapturedCompanyId { get; private set; }

        public Task<bool> GetAuctionVerticalEnabled(Guid companyId, CancellationToken ct = default)
        {
            CapturedCompanyId = companyId;
            return Task.FromResult(AuctionVerticalEnabled);
        }

        // Autres lectures du paramétrage non exercées par le service de la table TVA.
        public Task<TenantProfileDto?> GetTenantProfile(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<TenantProfileDto?>(null);

        public Task<FiscalSettingsDto?> GetFiscalSettings(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<FiscalSettingsDto?>(null);

        public Task<BillingMentionsDto?> GetBillingMentions(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<BillingMentionsDto?>(null);

        public Task<IReadOnlyList<PaAccountDto>> GetPaAccounts(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PaAccountDto>>(Array.Empty<PaAccountDto>());

        public Task<ExtractionScheduleDto?> GetExtractionSchedule(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<ExtractionScheduleDto?>(null);

        public Task<AlertThresholdsDto?> GetAlertThresholds(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<AlertThresholdsDto?>(null);

        public Task<Guid?> GetCurrentCompanyId(CancellationToken ct = default) =>
            Task.FromResult<Guid?>(null);

        /// <summary>Statut du tenant courant : null = pas de profil = ACTIF (defaut neutre des tests).</summary>
        public Task<string?> GetCurrentTenantStatut(CancellationToken ct = default) =>
            Task.FromResult<string?>(null);
    }
}
