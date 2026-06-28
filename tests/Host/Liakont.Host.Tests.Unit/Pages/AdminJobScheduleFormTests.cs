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
using Stratum.Common.Abstractions.Grid;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.UI;
using Stratum.Modules.Job.Contracts;
using Stratum.Modules.Job.Contracts.Commands;
using Stratum.Modules.Job.Contracts.DTOs;
using Stratum.Modules.Job.Contracts.Queries;
using Stratum.Modules.Job.Contracts.Services;
using Stratum.Modules.Job.Web.Pages;
using Xunit;

// FIX211 : formulaire de planification — liste FIXE des types (libellés FR), payload typé, helper cron.
public sealed class AdminJobScheduleFormTests : BunitContext
{
    private const string EmptyKey = "Test.EmptyTrigger";
    private const string DryRunKey = "Test.WithDryRun";

    public AdminJobScheduleFormTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddCommonUI();
        Services.AddSingleton<IStringLocalizer<SharedResources>>(new StubStringLocalizer());
        Services.AddScoped<IGridPreferenceService>(_ => new NullGridPreferenceService());
        Services.AddScoped<ISavedFilterService>(_ => new NullSavedFilterService());
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(JobPermissions.View, JobPermissions.ManageSchedules));
        Services.AddScoped<ISender>(_ => new NoopSender());
        Services.AddScoped<IScheduleQueries>(_ => new EmptyScheduleQueries());
        Services.AddScoped<ICronPreviewService>(_ => new FakeCronPreview());
        Services.AddScoped<IActorContextAccessor>(_ => new StubActorContextAccessor());

        // Défaut : aucun job « système » → toute planification est tenant-scopée (comportement actuel). Les
        // tests BUG-4b ré-enregistrent un FakeSystemScheduleHost qui marque un type comme système.
        Services.AddScoped<ISystemScheduleHost>(_ => new FakeSystemScheduleHost());
        var dryRunParam = new JobParameterDescriptor("DryRun", "Dry run", JobParameterKind.Boolean, false, "false", []);
        Services.AddScoped<IJobTypeCatalog>(_ => new FakeCatalog(
            new JobTypeDescriptor(EmptyKey, "Type sans paramètre", []),
            new JobTypeDescriptor(DryRunKey, "Type avec simulation", [dryRunParam])));
    }

    [Fact]
    public void Job_Type_Is_A_Fixed_Dropdown_With_French_Labels()
    {
        var cut = RenderCreate();

        var select = cut.Find("[data-testid='job-schedule-form-job-type']");
        select.NodeName.Should().Be("SELECT", "le type de job est une liste fixe, plus un champ texte libre");

        var optionTexts = select.QuerySelectorAll("option").Select(o => o.TextContent).ToList();
        optionTexts.Should().Contain("Type sans paramètre").And.Contain("Type avec simulation");
        optionTexts.Should().NotContain(t => t.Contains('.'), "jamais le FullName .NET dans les libellés");
    }

    [Fact]
    public void Selecting_A_Type_With_A_Boolean_Parameter_Shows_A_Typed_Field()
    {
        var cut = RenderCreate();

        cut.Find("[data-testid='job-schedule-form-job-type']").Change(DryRunKey);

        cut.FindAll("[data-testid='job-schedule-form-param-DryRun']").Should().ContainSingle();
        cut.FindAll("[data-testid='job-schedule-form-param-input-DryRun']").Should().ContainSingle();

        // Plus aucun éditeur de JSON brut.
        cut.FindAll("[data-testid='job-schedule-form-payload']").Should().BeEmpty();
    }

    [Fact]
    public void Selecting_A_Type_Without_Parameters_Hides_The_Payload()
    {
        var cut = RenderCreate();

        cut.Find("[data-testid='job-schedule-form-job-type']").Change(DryRunKey);
        cut.FindAll("[data-testid='job-schedule-form-param-DryRun']").Should().ContainSingle();

        // Bascule vers un type sans paramètre : le champ disparaît (payload masqué).
        cut.Find("[data-testid='job-schedule-form-job-type']").Change(EmptyKey);
        cut.FindAll("[data-testid='job-schedule-form-param-DryRun']").Should().BeEmpty();
    }

    [Fact]
    public void Cron_Preset_Fills_The_Cron_Expression_And_Timezone_Is_Stated()
    {
        var cut = RenderCreate();

        cut.FindAll("[data-testid='job-schedule-form-cron-preset']").Should().ContainSingle();

        cut.Find("[data-testid='job-schedule-form-cron-preset']").Change("15min");
        cut.Find("[data-testid='job-schedule-form-cron']").GetAttribute("value").Should().Be("*/15 * * * *");

        cut.Find("[data-testid='job-schedule-form-cron-preset']").Change("hourly");
        cut.Find("[data-testid='job-schedule-form-cron']").GetAttribute("value").Should().Be("0 * * * *");

        // RB6 : l'aperçu cron = PRÉVISION serveur (cron interprété en UTC) → UTC EXPLICITE, cohérent avec NextRunAt.
        // Assertion sur une LIGNE d'aperçu datée (et non le titre « (UTC) » : asserter "UTC" seul serait un faux-vert).
        cut.Markup.Should().Contain("11 juin 2026 à 01:00 UTC");
    }

    // BUG-4 volet A : un échec de création (ici : opérateur sans société courante) doit être VISIBLE.
    // Avant le correctif, MapError écrivait le champ _globalError du parent sans re-render → bannière jamais affichée.
    [Fact]
    public void Save_Failure_Without_Current_Company_Shows_A_Visible_Error()
    {
        Services.AddScoped<IActorContextAccessor>(_ => new StubActorContextAccessor(useDefaultCompany: false));

        var cut = RenderCreate();

        cut.Find("[data-testid='job-schedule-form-name']").Input("Ancrage quotidien");
        cut.Find("[data-testid='job-schedule-form-job-type']").Change(EmptyKey);
        cut.Find("[data-testid='job-schedule-form-cron']").Input("0 2 * * *");

        cut.Find("[data-testid='job-schedule-form-save-btn']").Click();

        cut.WaitForAssertion(
            () => cut.Find("[data-testid='job-schedule-form-error']").TextContent
                .Should().Contain("Aucune société sélectionnée"),
            TimeSpan.FromSeconds(5));
    }

    // BUG-4b volet B : un job SYSTÈME (fan-out tous tenants) doit être planifiable par un opérateur PLATEFORME
    // sans société courante — porté par la société porteuse système, PAS par la société de l'opérateur.
    [Fact]
    public void Save_Of_A_System_Job_Without_Current_Company_Targets_The_Host_Company()
    {
        Services.AddScoped<IActorContextAccessor>(_ => new StubActorContextAccessor(useDefaultCompany: false));
        var sender = new RecordingSender();
        Services.AddScoped<ISender>(_ => sender);
        Services.AddScoped<ISystemScheduleHost>(_ => new FakeSystemScheduleHost(systemJobType: EmptyKey));

        var cut = RenderCreate();

        cut.Find("[data-testid='job-schedule-form-name']").Input("Évaluation de la supervision");
        cut.Find("[data-testid='job-schedule-form-job-type']").Change(EmptyKey);
        cut.Find("[data-testid='job-schedule-form-cron']").Input("*/15 * * * *");

        cut.Find("[data-testid='job-schedule-form-save-btn']").Click();

        cut.WaitForAssertion(
            () => sender.Sent.OfType<CreateScheduleCommand>().Should().ContainSingle()
                .Which.CompanyId.Should().Be(FakeSystemScheduleHost.HostCompany),
            TimeSpan.FromSeconds(5));

        // Création réussie : aucune bannière d'erreur (le sysadmin n'est plus bloqué).
        cut.FindAll("[data-testid='job-schedule-form-error']").Should().BeEmpty();
    }

    // BUG-4b — INVARIANT vérifié ici : un opérateur disposant d'une société courante écrit TOUJOURS sur SA
    // société, JAMAIS sur la société porteuse plateforme (pas d'écriture hors de son périmètre). Comportement
    // INCHANGÉ par ce lot (la branche CompanyId courante prime). NB : la page d'admin des planifications est
    // gardée par la permission socle job.view, jamais accordée à un rôle tenant Liakont (RolePermissionCatalog,
    // matrice immuable ; cf. JobNavVisibilityFilter) → seul un super-admin l'ouvre. Le périmètre des types
    // exposés à un éventuel opérateur tenant (filtrage des fan-out système) est hors de ce lot (pré-existant).
    [Fact]
    public void Tenant_Operator_Choosing_A_System_Job_Stays_Scoped_To_Their_Company()
    {
        var sender = new RecordingSender();
        Services.AddScoped<ISender>(_ => sender);

        // Société courante présente (opérateur tenant) ; le type EmptyKey est « système » côté porteuse.
        Services.AddScoped<IActorContextAccessor>(_ => new StubActorContextAccessor(useDefaultCompany: true));
        Services.AddScoped<ISystemScheduleHost>(_ => new FakeSystemScheduleHost(systemJobType: EmptyKey));

        var cut = RenderCreate();

        cut.Find("[data-testid='job-schedule-form-name']").Input("Une planif");
        cut.Find("[data-testid='job-schedule-form-job-type']").Change(EmptyKey);
        cut.Find("[data-testid='job-schedule-form-cron']").Input("*/15 * * * *");

        cut.Find("[data-testid='job-schedule-form-save-btn']").Click();
        var tenantCompany = Guid.Parse("11111111-1111-1111-1111-111111111111");
        cut.WaitForAssertion(
            () =>
            {
                var cmd = sender.Sent.OfType<CreateScheduleCommand>().Should().ContainSingle().Subject;
                cmd.CompanyId.Should().Be(tenantCompany);
                cmd.CompanyId.Should().NotBe(FakeSystemScheduleHost.HostCompany);
            },
            TimeSpan.FromSeconds(5));
    }

    private IRenderedComponent<AdminJobScheduleForm> RenderCreate() =>
        Render<AdminJobScheduleForm>(p => p.Add(c => c.Id, (Guid?)null));

    private sealed class FakeCatalog : IJobTypeCatalog
    {
        private readonly IReadOnlyList<JobTypeDescriptor> _all;

        public FakeCatalog(params JobTypeDescriptor[] all) => _all = all;

        public IReadOnlyList<JobTypeDescriptor> GetAll() => _all;

        public JobTypeDescriptor? Find(string technicalKey) =>
            _all.FirstOrDefault(d => d.TechnicalKey == technicalKey);
    }

    private sealed class FakeSystemScheduleHost : ISystemScheduleHost
    {
        public static readonly Guid HostCompany = Guid.Parse("5c8ed001-0000-4000-b000-000000000001");

        private readonly string? _systemJobType;

        public FakeSystemScheduleHost(string? systemJobType = null) => _systemJobType = systemJobType;

        public Guid? CrossTenantHostCompanyId => HostCompany;

        public Guid? ResolveHostCompanyId(string jobType) =>
            _systemJobType is not null && jobType == _systemJobType ? HostCompany : null;
    }

    private sealed class RecordingSender : ISender
    {
        public List<object> Sent { get; } = [];

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Sent.Add(request);
            return Task.FromResult((TResponse)(object)Guid.NewGuid());
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            Sent.Add(request!);
            return Task.CompletedTask;
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            Task.FromResult<object?>(null);

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeCronPreview : ICronPreviewService
    {
        public CronValidationResult Validate(string cronExpression) =>
            string.IsNullOrWhiteSpace(cronExpression)
                ? new CronValidationResult(false, "obligatoire")
                : new CronValidationResult(true);

        public IReadOnlyList<DateTimeOffset> GetNextOccurrences(string cronExpression, int count = 5) =>
            Enumerable.Range(1, count)
                .Select(i => new DateTimeOffset(2026, 6, 11, i, 0, 0, TimeSpan.Zero))
                .ToList();
    }

    private sealed class EmptyScheduleQueries : IScheduleQueries
    {
        public Task<ScheduleDto?> GetByIdAsync(Guid scheduleId, CancellationToken ct = default) =>
            Task.FromResult<ScheduleDto?>(null);

        public Task<IReadOnlyList<ScheduleDto>> ListByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ScheduleDto>>([]);
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
        private static readonly Guid DefaultCompany = Guid.Parse("11111111-1111-1111-1111-111111111111");

        // useDefaultCompany=true → société courante présente (cas nominal) ; false → AUCUNE société (sysadmin/supervision).
        public StubActorContextAccessor(bool useDefaultCompany = true) =>
            Current = new StubActorContext(useDefaultCompany ? DefaultCompany : null);

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
