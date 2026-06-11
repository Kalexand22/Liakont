namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Stratum.Common.Abstractions.Grid;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.UI;
using Stratum.Modules.Job.Contracts;
using Stratum.Modules.Job.Contracts.DTOs;
using Stratum.Modules.Job.Contracts.Queries;
using Stratum.Modules.Job.Contracts.Services;
using Stratum.Modules.Job.Web.Pages;
using Xunit;

// FIX211 : page des EXÉCUTIONS de jobs (job.jobs). Tenant-scopée, filtres statut/type/période, libellés FR.
public sealed class AdminJobExecutionsTests : BunitContext
{
    private const string SupervisionKey = "Liakont.Modules.Supervision.Infrastructure.SupervisionEvaluationTrigger";
    private static readonly Guid TenantCompany = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public AdminJobExecutionsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddCommonUI();
        Services.AddSingleton<IStringLocalizer<SharedResources>>(new StubStringLocalizer());
        Services.AddScoped<IGridPreferenceService>(_ => new NullGridPreferenceService());
        Services.AddScoped<ISavedFilterService>(_ => new NullSavedFilterService());
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(JobPermissions.View));
        Services.AddScoped<IJobTypeCatalog>(_ => new FakeCatalog(
            new JobTypeDescriptor(SupervisionKey, "Évaluation de la supervision", [])));
        Services.AddScoped<IActorContextAccessor>(_ => new StubActorContextAccessor(TenantCompany));
    }

    [Fact]
    public void Renders_Filters_And_Rows_With_French_Status_And_Type_Label()
    {
        Services.AddScoped<IJobExecutionsQueries>(_ => new FakeExecutionsQueries(
            Job(SupervisionKey, "Completed")));

        var cut = Render<AdminJobExecutions>();

        // Barre de filtres (statut / type / période).
        cut.FindAll("[data-testid='job-executions-filters']").Should().ContainSingle();
        cut.FindAll("[data-testid='job-executions-filter-status']").Should().ContainSingle();
        cut.FindAll("[data-testid='job-executions-filter-type']").Should().ContainSingle();
        cut.FindAll("[data-testid='job-executions-filter-from']").Should().ContainSingle();
        cut.FindAll("[data-testid='job-executions-filter-to']").Should().ContainSingle();

        // Le type est rendu en libellé FR dans la ligne (la clé technique ne vit que dans l'attribut
        // value de l'option du filtre, jamais comme texte affiché — cf. JobTypeLabel, testé en unitaire).
        cut.Markup.Should().Contain("Évaluation de la supervision");
        var typeFilterValues = cut.Find("[data-testid='job-executions-filter-type']")
            .QuerySelectorAll("option").Select(o => o.TextContent).ToList();
        typeFilterValues.Should().NotContain(t => t.Contains(SupervisionKey), "le libellé d'option est en FR, pas le FullName");

        // Statut en français.
        cut.Markup.Should().Contain("Terminé");
    }

    [Fact]
    public void Loads_Executions_Scoped_To_The_Current_Tenant_Company()
    {
        var queries = new FakeExecutionsQueries(Job(SupervisionKey, "Running"));
        Services.AddScoped<IJobExecutionsQueries>(_ => queries);

        Render<AdminJobExecutions>();

        queries.Calls.Should().BeGreaterThan(0);
        queries.LastFilter!.CompanyId.Should().Be(TenantCompany, "la requête est tenant-scopée (CLAUDE.md n°9)");
    }

    [Fact]
    public void Without_A_Company_Nothing_Is_Queried_And_The_Empty_State_Shows()
    {
        var queries = new FakeExecutionsQueries(Job(SupervisionKey, "Completed"));
        Services.AddScoped<IJobExecutionsQueries>(_ => queries);

        // Acteur sans société : aucune requête tenant ne doit partir.
        Services.AddScoped<IActorContextAccessor>(_ => new StubActorContextAccessor(null));

        var cut = Render<AdminJobExecutions>();

        queries.Calls.Should().Be(0, "sans société courante, aucune requête tenant n'est émise");
        cut.FindAll("[data-testid='job-executions-empty']").Should().ContainSingle();
    }

    [Fact]
    public void Type_Query_Parameter_Preselects_The_Type_Filter()
    {
        Services.AddScoped<IJobExecutionsQueries>(_ => new FakeExecutionsQueries());
        var nav = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        nav.NavigateTo($"/admin/jobs/executions?type={Uri.EscapeDataString(SupervisionKey)}");

        var cut = Render<AdminJobExecutions>();

        // Le lien « Voir les exécutions » d'une planification pré-filtre par type.
        cut.Find("[data-testid='job-executions-filter-type']")
            .GetAttribute("value").Should().Be(SupervisionKey);
    }

    private static JobDto Job(string type, string status) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        Status = status,
        Priority = 0,
        MaxRetries = 3,
        RetryCount = 0,
        ScheduledAt = new DateTimeOffset(2026, 6, 11, 8, 0, 0, TimeSpan.Zero),
        StartedAt = new DateTimeOffset(2026, 6, 11, 8, 0, 5, TimeSpan.Zero),
        CompletedAt = new DateTimeOffset(2026, 6, 11, 8, 0, 9, TimeSpan.Zero),
        ErrorMessage = null,
        CompanyId = TenantCompany,
        CreatedAt = new DateTimeOffset(2026, 6, 11, 8, 0, 0, TimeSpan.Zero),
    };

    private sealed class FakeExecutionsQueries : IJobExecutionsQueries
    {
        private readonly IReadOnlyList<JobDto> _rows;

        public FakeExecutionsQueries(params JobDto[] rows) => _rows = rows;

        public int Calls { get; private set; }

        public JobExecutionsFilter? LastFilter { get; private set; }

        public Task<IReadOnlyList<JobDto>> ListAsync(JobExecutionsFilter filter, CancellationToken ct = default)
        {
            Calls++;
            LastFilter = filter;
            return Task.FromResult(_rows);
        }
    }

    private sealed class FakeCatalog : IJobTypeCatalog
    {
        private readonly IReadOnlyList<JobTypeDescriptor> _all;

        public FakeCatalog(params JobTypeDescriptor[] all) => _all = all;

        public IReadOnlyList<JobTypeDescriptor> GetAll() => _all;

        public JobTypeDescriptor? Find(string technicalKey) =>
            _all.FirstOrDefault(d => d.TechnicalKey == technicalKey);
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
