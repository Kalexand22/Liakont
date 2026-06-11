namespace Liakont.Host.Tests.Unit.PaAccounts;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.PaAccounts;
using Liakont.Host.Security;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.Transmission.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.Security;
using Xunit;

/// <summary>
/// Tests unitaires de <see cref="PaPublicationConsoleService"/> (FIX201) : publication du SIREN /
/// activation de la transmission. Vérifie que la demande est construite SANS rien inventer (SIREN →
/// <c>cin_scheme « 0002 »</c> ; date/type/taille fournis), que l'appel idempotent
/// <c>EnsureTaxReportSettingAsync</c> est émis, que l'opération est TRACÉE (audit), que la garde
/// <c>liakont.settings</c> et les pré-requis (profil, compte actif) refusent proprement, et que la lecture
/// d'état est DÉFENSIVE (ne lève pas si la PA est injoignable).
/// </summary>
public sealed class PaPublicationConsoleServiceTests
{
    private static readonly Guid Company = Guid.Parse("00000000-0000-4000-a000-000000000001");

    [Fact]
    public async Task PublishAsync_Builds_Request_With_Cin0002_And_Journals()
    {
        var client = new RecordingPaClient();
        var registry = new StubRegistry("Fake", client);
        var settings = new StubSettings
        {
            Profile = ProfileWithSiren("123456782"),
            Accounts = [ActiveAccount("Fake")],
        };
        var audit = new RecordingActivityLogger();
        var service = Service(registry, settings, audit, permission: true, today: new DateOnly(2026, 6, 11));

        var result = await service.PublishAsync(new PaPublicationFormModel
        {
            StartDate = new DateOnly(2026, 1, 1),
            TypeOperation = "LBS",
            EnterpriseSize = "PME",
            NafCode = "62",
        });

        result.Success.Should().BeTrue();
        client.Ensured.Should().ContainSingle();
        var request = client.Ensured.Single();
        request.CinScheme.Should().Be("0002", "le réglage est assigné au niveau SIREN (F05 §2)");
        request.StartDate.Should().Be(new DateOnly(2026, 1, 1));
        request.TypeOperation.Should().Be("LBS");
        request.EnterpriseSize.Should().Be("PME");
        request.NafCode.Should().Be("62");

        audit.Entries.Should().ContainSingle();
        var entry = audit.Entries.Single();
        entry.EntityType.Should().Be(PaPublicationAudit.EntityType);
        entry.ActivityType.Should().Be(PaPublicationAudit.PublishedActivity);
        entry.CompanyId.Should().Be(Company);
    }

    [Fact]
    public async Task PublishAsync_Without_Permission_Is_Refused_And_Does_Not_Call_The_Pa()
    {
        var client = new RecordingPaClient();
        var service = Service(
            new StubRegistry("Fake", client),
            new StubSettings { Profile = ProfileWithSiren("123456782"), Accounts = [ActiveAccount("Fake")] },
            new RecordingActivityLogger(),
            permission: false,
            today: new DateOnly(2026, 6, 11));

        var result = await service.PublishAsync(ValidForm());

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("liakont.settings");
        client.Ensured.Should().BeEmpty("aucun appel PA sans autorisation");
    }

    [Fact]
    public async Task PublishAsync_Without_Profile_Siren_Is_Refused()
    {
        var client = new RecordingPaClient();
        var service = Service(
            new StubRegistry("Fake", client),
            new StubSettings { Profile = null, Accounts = [ActiveAccount("Fake")] },
            new RecordingActivityLogger(),
            permission: true,
            today: new DateOnly(2026, 6, 11));

        var result = await service.PublishAsync(ValidForm());

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("SIREN");
        client.Ensured.Should().BeEmpty();
    }

    [Fact]
    public async Task PublishAsync_Without_Active_Account_Is_Refused()
    {
        var client = new RecordingPaClient();
        var service = Service(
            new StubRegistry("Fake", client),
            new StubSettings { Profile = ProfileWithSiren("123456782"), Accounts = [] },
            new RecordingActivityLogger(),
            permission: true,
            today: new DateOnly(2026, 6, 11));

        var result = await service.PublishAsync(ValidForm());

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("compte Plateforme Agréée actif");
        client.Ensured.Should().BeEmpty();
    }

    [Fact]
    public async Task PublishAsync_Missing_Form_Fields_Is_Refused()
    {
        var client = new RecordingPaClient();
        var service = Service(
            new StubRegistry("Fake", client),
            new StubSettings { Profile = ProfileWithSiren("123456782"), Accounts = [ActiveAccount("Fake")] },
            new RecordingActivityLogger(),
            permission: true,
            today: new DateOnly(2026, 6, 11));

        var result = await service.PublishAsync(new PaPublicationFormModel
        {
            StartDate = new DateOnly(2026, 1, 1),
            TypeOperation = "  ",
            EnterpriseSize = string.Empty,
        });

        result.Success.Should().BeFalse();
        client.Ensured.Should().BeEmpty("rien d'inventé : sans saisie, pas de publication");
    }

    [Fact]
    public async Task PublishAsync_Future_StartDate_Reports_Not_Yet_Active()
    {
        var client = new RecordingPaClient();
        var service = Service(
            new StubRegistry("Fake", client),
            new StubSettings { Profile = ProfileWithSiren("123456782"), Accounts = [ActiveAccount("Fake")] },
            new RecordingActivityLogger(),
            permission: true,
            today: new DateOnly(2026, 6, 11));

        var result = await service.PublishAsync(new PaPublicationFormModel
        {
            StartDate = new DateOnly(2026, 9, 1),
            TypeOperation = "LBS",
            EnterpriseSize = "PME",
        });

        result.Success.Should().BeTrue("le réglage est enregistré");
        result.Message.Should().Contain("sera active", "une date future = pas encore publié (F05 §2)");
        client.Ensured.Should().ContainSingle();
    }

    [Fact]
    public async Task GetStateAsync_Reports_Published_When_StartDate_Is_Set()
    {
        var client = new RecordingPaClient
        {
            CurrentSetting = new PaTaxReportSetting { StartDate = new DateOnly(2026, 1, 1) },
        };
        var service = Service(
            new StubRegistry("Fake", client),
            new StubSettings { Profile = ProfileWithSiren("123456782"), Accounts = [ActiveAccount("Fake")] },
            new RecordingActivityLogger(),
            permission: true,
            today: new DateOnly(2026, 6, 11));

        var state = await service.GetStateAsync();

        state.HasActiveAccount.Should().BeTrue();
        state.StateAvailable.Should().BeTrue();
        state.IsPublished.Should().BeTrue();
        state.StartDate.Should().Be(new DateOnly(2026, 1, 1));
        state.Siren.Should().Be("123456782");
    }

    [Fact]
    public async Task GetStateAsync_Reports_Unpublished_When_StartDate_Is_Null()
    {
        var client = new RecordingPaClient { CurrentSetting = new PaTaxReportSetting() };
        var service = Service(
            new StubRegistry("Fake", client),
            new StubSettings { Profile = ProfileWithSiren("123456782"), Accounts = [ActiveAccount("Fake")] },
            new RecordingActivityLogger(),
            permission: true,
            today: new DateOnly(2026, 6, 11));

        var state = await service.GetStateAsync();

        state.HasActiveAccount.Should().BeTrue();
        state.StateAvailable.Should().BeTrue();
        state.IsPublished.Should().BeFalse();
        state.StartDate.Should().BeNull();
    }

    [Fact]
    public async Task GetStateAsync_With_Future_StartDate_Is_Not_Yet_Published()
    {
        // Une date de début future = pas encore actif (même critère que le gating d'envoi, F05 §2) :
        // l'état ne doit PAS être « publié » (sinon la console contredirait le refus d'envoi).
        var client = new RecordingPaClient
        {
            CurrentSetting = new PaTaxReportSetting { StartDate = new DateOnly(2026, 9, 1) },
        };
        var service = Service(
            new StubRegistry("Fake", client),
            new StubSettings { Profile = ProfileWithSiren("123456782"), Accounts = [ActiveAccount("Fake")] },
            new RecordingActivityLogger(),
            permission: true,
            today: new DateOnly(2026, 6, 11));

        var state = await service.GetStateAsync();

        state.StateAvailable.Should().BeTrue();
        state.IsPublished.Should().BeFalse("une date future n'est pas encore active (gating d'envoi, F05 §2)");
        state.StartDate.Should().Be(new DateOnly(2026, 9, 1));
    }

    [Fact]
    public async Task GetStateAsync_Without_Active_Account_Reports_None()
    {
        var service = Service(
            new StubRegistry("Fake", new RecordingPaClient()),
            new StubSettings { Profile = ProfileWithSiren("123456782"), Accounts = [] },
            new RecordingActivityLogger(),
            permission: true,
            today: new DateOnly(2026, 6, 11));

        var state = await service.GetStateAsync();

        state.HasActiveAccount.Should().BeFalse();
        state.Siren.Should().Be("123456782");
    }

    [Fact]
    public async Task GetStateAsync_Degrades_When_The_Pa_Is_Unreachable()
    {
        // Résoudre un client vivant peut lever (clé absente / PA injoignable) : l'écran ne doit pas échouer.
        var service = Service(
            new ThrowingRegistry("Fake"),
            new StubSettings { Profile = ProfileWithSiren("123456782"), Accounts = [ActiveAccount("Fake")] },
            new RecordingActivityLogger(),
            permission: true,
            today: new DateOnly(2026, 6, 11));

        var state = await service.GetStateAsync();

        state.HasActiveAccount.Should().BeTrue();
        state.StateAvailable.Should().BeFalse("état dégradé, jamais une exception (précédent API01c)");
        state.PluginType.Should().Be("Fake");
    }

    private static PaPublicationFormModel ValidForm() => new()
    {
        StartDate = new DateOnly(2026, 1, 1),
        TypeOperation = "LBS",
        EnterpriseSize = "PME",
    };

    private static TenantProfileDto ProfileWithSiren(string siren) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Company,
        Siren = siren,
        RaisonSociale = "Société Fictive",
        Street = "1 rue de l'Exemple",
        PostalCode = "35000",
        City = "Rennes",
        Country = "FR",
        Statut = "Actif",
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private static PaAccountDto ActiveAccount(string pluginType) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Company,
        PluginType = pluginType,
        Environment = "Staging",
        AccountIdentifiers = "{}",
        HasApiKey = false,
        IsActive = true,
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private static PaPublicationConsoleService Service(
        IPaClientRegistry registry,
        StubSettings settings,
        RecordingActivityLogger audit,
        bool permission,
        DateOnly today) =>
        new(
            new StubActorAccessor(Company, "tenant-test"),
            settings,
            registry,
            audit,
            new StubPermissionService(permission),
            NullLogger<PaPublicationConsoleService>.Instance,
            new FixedTimeProvider(today));

    private sealed class StubActorAccessor : IActorContextAccessor
    {
        public StubActorAccessor(Guid? companyId, string tenantId) =>
            Current = new StubActor(companyId, tenantId);

        public IActorContext Current { get; }

        private sealed class StubActor : IActorContext
        {
            public StubActor(Guid? companyId, string tenantId)
            {
                CompanyId = companyId;
                TenantId = tenantId;
            }

            public Guid UserId => Guid.Empty;

            public Guid CorrelationId => Guid.Empty;

            public bool IsAuthenticated => true;

            public string? DisplayName => "Opérateur Test";

            public string? Email => null;

            public Guid? CompanyId { get; }

            public string? Timezone => null;

            public string? Language => "fr";

            public string? TenantId { get; }
        }
    }

    private sealed class StubSettings : ITenantSettingsQueries
    {
        public TenantProfileDto? Profile { get; init; }

        public IReadOnlyList<PaAccountDto> Accounts { get; init; } = Array.Empty<PaAccountDto>();

        public Task<TenantProfileDto?> GetTenantProfile(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult(Profile);

        public Task<FiscalSettingsDto?> GetFiscalSettings(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<FiscalSettingsDto?>(null);

        public Task<IReadOnlyList<PaAccountDto>> GetPaAccounts(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult(Accounts);

        public Task<ExtractionScheduleDto?> GetExtractionSchedule(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<ExtractionScheduleDto?>(null);

        public Task<AlertThresholdsDto?> GetAlertThresholds(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<AlertThresholdsDto?>(null);

        public Task<bool> GetAuctionVerticalEnabled(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<Guid?> GetCurrentCompanyId(CancellationToken ct = default) =>
            Task.FromResult<Guid?>(Company);
    }

    private sealed class StubRegistry : IPaClientRegistry
    {
        private readonly string _type;
        private readonly IPaClient _client;

        public StubRegistry(string type, IPaClient client)
        {
            _type = type;
            _client = client;
        }

        public IReadOnlyCollection<string> RegisteredTypes => new[] { _type };

        public IPaClient Resolve(PaAccountDescriptor account) => _client;

        public bool IsRegistered(string paType) => string.Equals(paType, _type, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ThrowingRegistry : IPaClientRegistry
    {
        private readonly string _type;

        public ThrowingRegistry(string type) => _type = type;

        public IReadOnlyCollection<string> RegisteredTypes => new[] { _type };

        public IPaClient Resolve(PaAccountDescriptor account) =>
            throw new InvalidOperationException("clé API absente (simulé)");

        public bool IsRegistered(string paType) => string.Equals(paType, _type, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RecordingPaClient : IPaClient
    {
        public List<PaTaxReportSettingRequest> Ensured { get; } = [];

        public PaTaxReportSetting CurrentSetting { get; set; } = new();

        public PaCapabilities Capabilities => new() { PaName = "FakeTest" };

        public Task EnsureTaxReportSettingAsync(PaTaxReportSettingRequest request, CancellationToken cancellationToken = default)
        {
            Ensured.Add(request);
            CurrentSetting = new PaTaxReportSetting
            {
                NafCode = request.NafCode,
                StartDate = request.StartDate,
                TypeOperation = request.TypeOperation,
                EnterpriseSize = request.EnterpriseSize,
                CinScheme = request.CinScheme,
            };
            return Task.CompletedTask;
        }

        public Task<PaTaxReportSetting> GetTaxReportSettingAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CurrentSetting);

        public Task<PaSendResult> SendDocumentAsync(Liakont.Agent.Contracts.Pivot.PivotDocumentDto document, bool sendAfterImport = true, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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

        public Task<PaGeneratedDocument> GetGeneratedDocumentAsync(string paDocumentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubPermissionService : IPermissionService
    {
        private readonly bool _granted;

        public StubPermissionService(bool granted) => _granted = granted;

        public event Action? OnPermissionsChanged
        {
            add { }
            remove { }
        }

        public bool HasPermission(string permission) =>
            _granted && permission == LiakontPermissions.Settings;
    }

    private sealed class RecordingActivityLogger : Stratum.Common.Abstractions.Audit.IActivityLogger
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task LogActivityAsync(
            string entityType,
            string entityId,
            string activityType,
            string description,
            string actorId,
            object? metadata = null,
            Guid? companyId = null,
            CancellationToken cancellationToken = default)
        {
            Entries.Add(new AuditEntry(entityType, entityId, activityType, companyId));
            return Task.CompletedTask;
        }
    }

    private sealed record AuditEntry(string EntityType, string EntityId, string ActivityType, Guid? CompanyId);

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateOnly today) =>
            _now = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
