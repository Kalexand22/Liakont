namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.AgentManagement;
using Liakont.Host.Components.Pages;
using Liakont.Modules.Ingestion.Contracts.DTOs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Stratum.Common.Abstractions.Grid;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.UI;
using Xunit;

/// <summary>
/// Tests bUnit de la page « Gestion des agents » (WEB09) : page RÉSERVÉE au paramétrage (accès refusé en
/// lecture seule), liste du parc bâtie sur DeclaredListPage (états Actif / Muet / Révoqué, action
/// d'enregistrement proposée), état vide explicite et bandeau d'erreur sur échec de chargement. Le détail des
/// dialogues (clé une fois, confirmations) est couvert par les tests des composants ; le service est remplacé
/// par un faux : on prouve le WIRING page ↔ service ↔ permission.
/// </summary>
public sealed class AgentsTests : BunitContext
{
    public AgentsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();

        // Services réels du design-system (DeclaredListPage), localisation + contexte acteur stubbés, et
        // préférences de grille / filtres enregistrés en no-op — comme les autres tests de pages de liste.
        Services.AddCommonUI();
        Services.AddSingleton<IStringLocalizer<SharedResources>>(new StubStringLocalizer());
        Services.AddScoped<IActorContextAccessor>(_ => new StubActorContextAccessor());
        Services.AddScoped<IGridPreferenceService>(_ => new NullGridPreferenceService());
        Services.AddScoped<ISavedFilterService>(_ => new NullSavedFilterService());
    }

    [Fact]
    public void Without_settings_permission_shows_denied_and_no_list_or_register_action()
    {
        Use(FakeAgentManagementService.Returning(Line("Poste A")), canManage: false);

        var cut = Render<Agents>();

        cut.FindAll("[data-testid='agents-denied']").Should().ContainSingle(
            "la gestion des agents est réservée au paramétrage (liakont.settings)");
        cut.FindAll("[data-testid='agents-register-btn']").Should().BeEmpty();
        cut.Markup.Should().NotContain("Poste A", "aucune donnée n'est chargée sans la permission");
    }

    [Fact]
    public void With_settings_lists_agents_with_state_badges_and_register_action()
    {
        Use(
            FakeAgentManagementService.Returning(
                Line("Poste actif", revoked: false, silent: false),
                Line("Poste muet", revoked: false, silent: true),
                Line("Poste retiré", revoked: true, silent: false)),
            canManage: true);

        var cut = Render<Agents>();

        cut.FindAll("[data-testid='agents-denied']").Should().BeEmpty();
        cut.FindAll("[data-testid='agents-register-btn']").Should().ContainSingle();

        cut.Markup.Should().Contain("Poste actif").And.Contain("Poste muet").And.Contain("Poste retiré");

        // États rendus par le ColumnTemplate (badges) : Actif / Muet (alerte) / Révoqué.
        cut.Markup.Should().Contain("Actif").And.Contain("Muet").And.Contain("Révoqué");
    }

    [Fact]
    public void Empty_fleet_shows_explicit_empty_state()
    {
        Use(FakeAgentManagementService.Returning(), canManage: true);

        var cut = Render<Agents>();

        cut.FindAll("[data-testid='agents-empty']").Should().ContainSingle();
        cut.FindAll("[data-testid='agents-error']").Should().BeEmpty();
    }

    [Fact]
    public void Load_failure_shows_error_banner_and_not_the_list()
    {
        Use(FakeAgentManagementService.Throwing(), canManage: true);

        var cut = Render<Agents>();

        cut.FindAll("[data-testid='agents-error']").Should().ContainSingle();
        cut.FindAll("[data-testid='agents-register-btn']").Should().BeEmpty("la liste n'est pas exposée sur échec de chargement");
    }

    private static AgentConsoleLine Line(string name, bool revoked = false, bool silent = false) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        KeyPrefix = "lk_pub",
        IsRevoked = revoked,
        IsSilent = silent,
        LastSeenUtc = new DateTimeOffset(2026, 6, 8, 10, 0, 0, TimeSpan.Zero),
        Version = "1.0.0",
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private void Use(IAgentManagementConsoleService service, bool canManage)
    {
        Services.AddScoped<IAgentManagementConsoleService>(_ => service);
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(canManage));
    }

    private sealed class FakeAgentManagementService : IAgentManagementConsoleService
    {
        private readonly IReadOnlyList<AgentConsoleLine>? _agents;
        private readonly bool _throws;

        private FakeAgentManagementService(IReadOnlyList<AgentConsoleLine>? agents, bool throws)
        {
            _agents = agents;
            _throws = throws;
        }

        public static FakeAgentManagementService Returning(params AgentConsoleLine[] agents) => new(agents, throws: false);

        public static FakeAgentManagementService Throwing() => new(null, throws: true);

        public Task<IReadOnlyList<AgentConsoleLine>> ListAsync(CancellationToken cancellationToken = default)
        {
            if (_throws)
            {
                throw new InvalidOperationException("Échec simulé de chargement du parc d'agents.");
            }

            return Task.FromResult(_agents!);
        }

        public Task<AgentKeyIssuedResult> RegisterAsync(string? name, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentKeyIssuedResult(
                AgentActionStatus.Succeeded,
                new AgentKeyIssuedDto { AgentId = Guid.NewGuid(), KeyPrefix = "lk", FullKey = "lk.secret" }));

        public Task<AgentActionStatus> RevokeAsync(Guid agentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(AgentActionStatus.Succeeded);

        public Task<AgentKeyIssuedResult> RotateKeyAsync(Guid agentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentKeyIssuedResult(
                AgentActionStatus.Succeeded,
                new AgentKeyIssuedDto { AgentId = agentId, KeyPrefix = "lk2", FullKey = "lk2.secret" }));
    }

    private sealed class FakePermissionService : IPermissionService
    {
        private readonly bool _canManage;

        public FakePermissionService(bool canManage) => _canManage = canManage;

        public event Action? OnPermissionsChanged
        {
            add { }
            remove { }
        }

        public bool HasPermission(string permission) =>
            _canManage && string.Equals(permission, "liakont.settings", StringComparison.Ordinal);
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
        public IActorContext Current { get; } = new StubActorContext();

        private sealed class StubActorContext : IActorContext
        {
            public Guid UserId => Guid.Empty;

            public Guid CorrelationId => Guid.Empty;

            public bool IsAuthenticated => true;

            public string? DisplayName => "Test";

            public string? Email => null;

            public Guid? CompanyId => null;

            public string? Timezone => null;

            public string? Language => "fr";

            public string? TenantId => "tenant-test";
        }
    }
}
