namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using Stratum.Common.Abstractions.Grid;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.UI;
using Stratum.Common.UI.Time;
using Stratum.Modules.Job.Contracts;
using Stratum.Modules.Job.Contracts.DTOs;
using Stratum.Modules.Job.Contracts.Queries;
using Stratum.Modules.Job.Contracts.Services;
using Stratum.Modules.Job.Web.Pages;
using Xunit;

// FIX211 : page socle des planifications, MODIFIÉE par l'item (colonne « Type de job » en libellé FR + action
// de ligne « Voir les exécutions ») → entre dans le périmètre de test (CLAUDE.md règle 19).
public sealed class AdminJobSchedulesTests : BunitContext
{
    private const string SupervisionKey = "Liakont.Modules.Supervision.Infrastructure.SupervisionEvaluationTrigger";
    private static readonly Guid TenantCompany = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public AdminJobSchedulesTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddCommonUI();
        Services.AddSingleton<IStringLocalizer<SharedResources>>(new StubStringLocalizer());
        Services.AddScoped<IGridPreferenceService>(_ => new NullGridPreferenceService());
        Services.AddScoped<ISavedFilterService>(_ => new NullSavedFilterService());
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(JobPermissions.View, JobPermissions.ManageSchedules));
        Services.AddScoped<ISender>(_ => new NoopSender());
        Services.AddScoped<IActorContextAccessor>(_ => new StubActorContextAccessor(TenantCompany));
        Services.AddScoped<IJobTypeCatalog>(_ => new FakeCatalog(
            new JobTypeDescriptor(SupervisionKey, "Évaluation de la supervision", [])));
        Services.AddScoped<IScheduleQueries>(_ => new FakeScheduleQueries(Schedule(SupervisionKey)));
        Services.AddScoped<ISystemScheduleHost>(_ => new FakeSystemScheduleHost());
    }

    [Fact]
    public void Job_Type_Column_Renders_The_French_Label_Never_The_FullName()
    {
        var cut = Render<AdminJobSchedules>();

        cut.Markup.Should().Contain("Évaluation de la supervision");
        cut.Markup.Should().NotContain(SupervisionKey, "la colonne affiche le libellé FR, jamais le FullName .NET");

        // BUG-25 : NextRunAt passe par LiakontDate (fuseau navigateur), comme LastRunAt. Ici le fuseau n'est pas
        // résolu (pas de sonde JS en bUnit) → repli UTC EXPLICITE (jamais une heure SERVEUR muette). Le rendu au
        // fuseau navigateur résolu est prouvé par Next_And_Last_Run_Render_In_The_Same_Browser_Timezone.
        cut.Markup.Should().Contain("11/06/2026 08:15 UTC");
    }

    [Fact]
    public void Next_And_Last_Run_Render_In_The_Same_Browser_Timezone()
    {
        // BUG-25 : « Prochaine » et « Dernière exécution » au MÊME fuseau (navigateur) — sinon la prochaine (UTC)
        // paraissait ANTÉRIEURE à la dernière (locale) pour un job qui vient de tourner. Fuseau navigateur résolu
        // (Europe/Paris, UTC+2 en juin) : les DEUX instants UTC sont convertis en heure locale, aucun suffixe UTC.
        var paris = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Romance Standard Time" : "Europe/Paris");
        Services.AddScoped<IBrowserTimeZone>(_ => new ResolvedBrowserTimeZone(paris));
        Services.AddScoped<IScheduleQueries>(_ => new FakeScheduleQueries(ScheduleWithLastRun(SupervisionKey)));

        var cut = Render<AdminJobSchedules>();

        // 08:15 UTC → 10:15 Paris (prochaine) ; 07:50 UTC → 09:50 Paris (dernière) : tous deux LOCAUX.
        cut.Markup.Should().Contain("11/06/2026 10:15").And.Contain("11/06/2026 09:50");
        cut.Markup.Should().NotContain("08:15 UTC", "la prochaine exécution n'est plus rendue en UTC mais au fuseau navigateur")
            .And.NotContain("07:50 UTC", "la dernière exécution reste au fuseau navigateur");
    }

    [Fact]
    public void Voir_Les_Executions_Navigates_To_The_Executions_Page_Filtered_By_Type()
    {
        var cut = Render<AdminJobSchedules>();

        cut.Find("[data-testid='quick-action-executions']").Click();

        var nav = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        var expected = $"/admin/jobs/executions?type={Uri.EscapeDataString(SupervisionKey)}";
        nav.Uri.Should().EndWith(expected, "le lien planification → exécutions pré-filtre par le type");
    }

    // BUG-4b volet B : un super-admin cross-tenant (sans société courante) doit consulter les planifications
    // SYSTÈME via la société porteuse — au lieu d'une liste vide (avant : CompanyId null ⇒ retour []).
    [Fact]
    public void Cross_Tenant_Admin_Without_Company_Lists_The_System_Host_Company_Schedules()
    {
        var recording = new RecordingScheduleQueries(Schedule(SupervisionKey));
        Services.AddScoped<IActorContextAccessor>(_ => new StubActorContextAccessor(null));
        Services.AddScoped<IScheduleQueries>(_ => recording);
        Services.AddScoped<ISystemScheduleHost>(_ => new FakeSystemScheduleHost());

        var cut = Render<AdminJobSchedules>();

        cut.WaitForAssertion(
            () => recording.ListedCompanies.Should().Contain(FakeSystemScheduleHost.HostCompany),
            TimeSpan.FromSeconds(5));
        cut.Markup.Should().Contain(
            "Évaluation de la supervision",
            "l'opérateur plateforme voit les planifications système (liste non vide)");
    }

    private static ScheduleDto Schedule(string jobType) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Supervision",
        CronExpression = "*/15 * * * *",
        JobType = jobType,
        PayloadTemplate = "{}",
        IsActive = true,
        NextRunAt = new DateTimeOffset(2026, 6, 11, 8, 15, 0, TimeSpan.Zero),
        LastRunAt = null,
        CompanyId = TenantCompany,
        CreatedAt = new DateTimeOffset(2026, 6, 11, 8, 0, 0, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2026, 6, 11, 8, 0, 0, TimeSpan.Zero),
    };

    // BUG-25 : planification avec une DERNIÈRE exécution (07:50 UTC) ET une PROCHAINE (08:15 UTC) pour comparer
    // les deux colonnes au même fuseau navigateur.
    private static ScheduleDto ScheduleWithLastRun(string jobType)
    {
        var schedule = Schedule(jobType);
        return schedule with { LastRunAt = new DateTimeOffset(2026, 6, 11, 7, 50, 0, TimeSpan.Zero) };
    }

    private sealed class FakeScheduleQueries : IScheduleQueries
    {
        private readonly IReadOnlyList<ScheduleDto> _rows;

        public FakeScheduleQueries(params ScheduleDto[] rows) => _rows = rows;

        public Task<ScheduleDto?> GetByIdAsync(Guid scheduleId, CancellationToken ct = default) =>
            Task.FromResult(_rows.FirstOrDefault(s => s.Id == scheduleId));

        public Task<IReadOnlyList<ScheduleDto>> ListByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult(_rows);
    }

    private sealed class RecordingScheduleQueries : IScheduleQueries
    {
        private readonly IReadOnlyList<ScheduleDto> _rows;

        public RecordingScheduleQueries(params ScheduleDto[] rows) => _rows = rows;

        public List<Guid> ListedCompanies { get; } = [];

        public Task<ScheduleDto?> GetByIdAsync(Guid scheduleId, CancellationToken ct = default) =>
            Task.FromResult(_rows.FirstOrDefault(s => s.Id == scheduleId));

        public Task<IReadOnlyList<ScheduleDto>> ListByCompanyAsync(Guid companyId, CancellationToken ct = default)
        {
            ListedCompanies.Add(companyId);
            return Task.FromResult(_rows);
        }
    }

    private sealed class FakeSystemScheduleHost : ISystemScheduleHost
    {
        public static readonly Guid HostCompany = Guid.Parse("5c8ed001-0000-4000-b000-000000000001");

        public Guid? CrossTenantHostCompanyId => HostCompany;

        public Guid? ResolveHostCompanyId(string jobType) => null;
    }

    // BUG-25 : fuseau navigateur DÉJÀ résolu (LiakontDate rend alors en heure locale, sans suffixe UTC).
    private sealed class ResolvedBrowserTimeZone : IBrowserTimeZone
    {
        public ResolvedBrowserTimeZone(TimeZoneInfo zone) => Zone = zone;

        public event Action? Resolved
        {
            add { }
            remove { }
        }

        public TimeZoneInfo? Zone { get; }

        public bool IsResolved => true;

        public Task EnsureResolvedAsync(IJSRuntime js, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeCatalog : IJobTypeCatalog
    {
        private readonly IReadOnlyList<JobTypeDescriptor> _all;

        public FakeCatalog(params JobTypeDescriptor[] all) => _all = all;

        public IReadOnlyList<JobTypeDescriptor> GetAll() => _all;

        public JobTypeDescriptor? Find(string technicalKey) =>
            _all.FirstOrDefault(d => d.TechnicalKey == technicalKey);
    }

    private sealed class NoopSender : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            Task.FromResult<TResponse>(default!);

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            Task.CompletedTask;

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            Task.FromResult<object?>(null);

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakePermissionService : IPermissionService
    {
        private readonly HashSet<string> _permissions;

        public FakePermissionService(params string[] permissions) =>
            _permissions = new HashSet<string>(permissions, StringComparer.Ordinal);

        public event Action? OnPermissionsChanged
        {
            add { }
            remove { }
        }

        public bool HasPermission(string permission) => _permissions.Contains(permission);
    }

    private sealed class NullSavedFilterService : ISavedFilterService
    {
        public Task<IReadOnlyList<SavedFilter>> ListAsync(Guid userId, string gridKey, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SavedFilter>>([]);

        public Task<SavedFilter?> GetAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<SavedFilter?>(null);

        public Task<SavedFilter> SaveAsync(SavedFilter filter, CancellationToken ct = default) =>
            Task.FromResult(filter);

        public Task DeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;

        public Task SetDefaultAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullGridPreferenceService : IGridPreferenceService
    {
        public Task<UserGridPreference?> GetPreferenceAsync(Guid userId, string gridKey, CancellationToken ct = default) =>
            Task.FromResult<UserGridPreference?>(null);

        public Task SavePreferenceAsync(Guid userId, string gridKey, IReadOnlyList<string> columnKeys, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SaveViewPreferenceAsync(Guid userId, string gridKey, ViewKind viewKind, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SaveFilterStateAsync(Guid userId, string gridKey, string? filterStateJson, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SaveColumnWidthsAsync(Guid userId, string gridKey, IReadOnlyDictionary<string, string> columnWidths, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class StubStringLocalizer : IStringLocalizer<SharedResources>
    {
        public LocalizedString this[string name] => new(name, name, resourceNotFound: true);

        public LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(System.Globalization.CultureInfo.InvariantCulture, name, arguments), resourceNotFound: true);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    private sealed class StubActorContextAccessor : IActorContextAccessor
    {
        public StubActorContextAccessor(Guid? companyId) => Current = new StubActorContext(companyId);

        public IActorContext Current { get; }

        private sealed class StubActorContext : IActorContext
        {
            public StubActorContext(Guid? companyId) => CompanyId = companyId;

            public Guid UserId => Guid.Empty;

            public Guid CorrelationId => Guid.Empty;

            public bool IsAuthenticated => true;

            public string? DisplayName => "Admin";

            public string? Email => null;

            public Guid? CompanyId { get; }

            public string? Timezone => null;

            public string? Language => "fr";

            public string? TenantId => "tenant-test";
        }
    }
}
