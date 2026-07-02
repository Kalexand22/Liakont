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
        Services.AddScoped<ISystemScheduleHost>(_ => new FakeSystemScheduleHost(FakeSystemScheduleHost.HostCompany));
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

        // RB6 : horodatages d'exécution au fuseau du NAVIGATEUR ; en bUnit la sonde n'est pas rendue → repli
        // UTC EXPLICITE (les exécutions du fake sont à 08:00). Capte une régression de câblage/nullable/suffixe.
        cut.Markup.Should().Contain("11/06/2026 08:00 UTC");
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

    // BUG-29 (symétrique de BUG-4b) : un super-admin cross-tenant (sans société courante) consulte les EXÉCUTIONS
    // des jobs SYSTÈME via la société porteuse — au lieu d'une liste vide. Sans ce repli, une planification système
    // (fan-out tous tenants, ex. e-reporting B2C) « tourne bien » mais l'écran des exécutions reste vide.
    [Fact]
    public void Cross_Tenant_Admin_Without_Company_Queries_The_System_Host_Company()
    {
        var queries = new FakeExecutionsQueries(Job(SupervisionKey, "Completed"));
        Services.AddScoped<IJobExecutionsQueries>(_ => queries);
        Services.AddScoped<IActorContextAccessor>(_ => new StubActorContextAccessor(null));

        Render<AdminJobExecutions>();

        queries.Calls.Should().BeGreaterThan(0, "l'opérateur plateforme consulte les exécutions système (pas de liste vide)");
        queries.LastFilter!.CompanyId.Should().Be(
            FakeSystemScheduleHost.HostCompany,
            "sans société courante, le repli cible la société porteuse système (BUG-29)");
    }

    // Socle nu (aucun job système : CrossTenantHostCompanyId null) ET aucune société courante : rien à consulter
    // → aucune requête, état vide. Garde-fou : le repli BUG-29 ne fabrique jamais une requête fantôme.
    [Fact]
    public void Without_A_Company_And_Without_A_System_Host_Nothing_Is_Queried()
    {
        var queries = new FakeExecutionsQueries(Job(SupervisionKey, "Completed"));
        Services.AddScoped<IJobExecutionsQueries>(_ => queries);
        Services.AddScoped<IActorContextAccessor>(_ => new StubActorContextAccessor(null));
        Services.AddScoped<ISystemScheduleHost>(_ => new FakeSystemScheduleHost(null));

        var cut = Render<AdminJobExecutions>();

        queries.Calls.Should().Be(0, "sans société courante NI porteuse système, aucune requête n'est émise");
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

    private sealed class FakeSystemScheduleHost : ISystemScheduleHost
    {
        public static readonly Guid HostCompany = Guid.Parse("5c8ed001-0000-4000-b000-000000000001");

        private readonly Guid? _hostCompany;

        public FakeSystemScheduleHost(Guid? hostCompany) => _hostCompany = hostCompany;

        public Guid? CrossTenantHostCompanyId => _hostCompany;

        public Guid? ResolveHostCompanyId(string jobType) => null;
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
