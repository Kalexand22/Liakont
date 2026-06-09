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

    [Fact]
    public void Registering_an_agent_displays_the_one_time_key_and_reloads_the_list()
    {
        var service = FakeAgentManagementService.Returning(Line("Poste A"));
        service.RegisterResult = new AgentKeyIssuedResult(
            AgentActionStatus.Succeeded,
            new AgentKeyIssuedDto { AgentId = Guid.NewGuid(), KeyPrefix = "lk_pub", FullKey = "lk_pub.secret-xyz" });
        Use(service, canManage: true);

        var cut = Render<Agents>();
        service.ListCalls.Should().Be(1);

        cut.Find("[data-testid='agents-register-btn']").Click();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='agent-register-name']").Should().ContainSingle());
        cut.Find("[data-testid='agent-register-name']").Input("Poste comptable");
        cut.Find("[data-testid='agent-register-submit']").Click();

        cut.WaitForAssertion(() =>
        {
            service.RegisterCalls.Should().Be(1);
            service.LastRegisterName.Should().Be("Poste comptable");
            cut.Find("[data-testid='agent-register-key']").TextContent.Should().Contain("lk_pub.secret-xyz");
            service.ListCalls.Should().Be(2, "la liste est rechargée après l'enregistrement");
        });
    }

    [Fact]
    public void Register_failure_shows_a_french_error_and_no_key()
    {
        var service = FakeAgentManagementService.Returning(Line("Poste A"));
        service.RegisterResult = new AgentKeyIssuedResult(AgentActionStatus.Failed, IssuedKey: null);
        Use(service, canManage: true);

        var cut = Render<Agents>();
        cut.Find("[data-testid='agents-register-btn']").Click();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='agent-register-name']").Should().ContainSingle());
        cut.Find("[data-testid='agent-register-name']").Input("Poste comptable");
        cut.Find("[data-testid='agent-register-submit']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='agent-register-error']").TextContent.Should().Contain("Réessayez plus tard");
            cut.FindAll("[data-testid='agent-register-key']").Should().BeEmpty();
        });
    }

    [Fact]
    public void Rotating_via_row_action_opens_the_dialog_and_shows_the_new_key_on_confirm()
    {
        var service = FakeAgentManagementService.Returning(Line("Poste A"));
        service.RotateResult = new AgentKeyIssuedResult(
            AgentActionStatus.Succeeded,
            new AgentKeyIssuedDto { AgentId = Guid.NewGuid(), KeyPrefix = "lk2", FullKey = "lk2.secret-new" });
        Use(service, canManage: true);

        var cut = Render<Agents>();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='quick-action-rotate']").Should().NotBeEmpty());
        cut.FindAll("[data-testid='quick-action-rotate']")[0].Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='agent-rotate-dialog']").Should().ContainSingle());
        cut.Find("[data-testid='agent-rotate-confirm']").Click();

        cut.WaitForAssertion(() =>
        {
            service.RotateCalls.Should().Be(1);
            cut.Find("[data-testid='agent-rotate-key']").TextContent.Should().Contain("lk2.secret-new");
        });
    }

    [Fact]
    public void Rotating_a_revoked_agent_race_shows_the_domain_conflict_message()
    {
        var service = FakeAgentManagementService.Returning(Line("Poste A"));
        service.RotateResult = new AgentKeyIssuedResult(
            AgentActionStatus.Conflict,
            IssuedKey: null,
            ErrorMessage: "Impossible de faire pivoter la clé d'un agent révoqué.");
        Use(service, canManage: true);

        var cut = Render<Agents>();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='quick-action-rotate']").Should().NotBeEmpty());
        cut.FindAll("[data-testid='quick-action-rotate']")[0].Click();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='agent-rotate-dialog']").Should().ContainSingle());
        cut.Find("[data-testid='agent-rotate-confirm']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='agent-rotate-error']").TextContent.Should().Contain("agent révoqué");
            cut.FindAll("[data-testid='agent-rotate-key']").Should().BeEmpty("aucune clé n'est émise sur conflit");
        });
    }

    [Fact]
    public void Revoking_via_row_action_confirms_calls_the_service_and_reloads()
    {
        var service = FakeAgentManagementService.Returning(Line("Poste A"));
        Use(service, canManage: true);

        var cut = Render<Agents>();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='quick-action-revoke']").Should().NotBeEmpty());
        cut.FindAll("[data-testid='quick-action-revoke']")[0].Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='agent-revoke-dialog']").Should().ContainSingle());
        cut.Find("[data-testid='agent-revoke-confirm']").Click();

        cut.WaitForAssertion(() =>
        {
            service.RevokeCalls.Should().Be(1);
            cut.FindAll("[data-testid='agent-revoke-dialog']").Should().BeEmpty("le dialogue se ferme après succès");
            service.ListCalls.Should().Be(2, "la liste est rechargée après la révocation");
        });
    }

    [Fact]
    public void Issued_key_stays_visible_when_the_post_action_refresh_fails()
    {
        // Le rechargement post-enregistrement échoue : la clé unique vient d'être affichée et NE DOIT PAS
        // disparaître. La page ne bascule PAS sur le bandeau d'erreur (toast non bloquant) ; le dialogue reste.
        var service = FakeAgentManagementService.Returning(Line("Poste A"));
        service.RegisterResult = new AgentKeyIssuedResult(
            AgentActionStatus.Succeeded,
            new AgentKeyIssuedDto { AgentId = Guid.NewGuid(), KeyPrefix = "lk_pub", FullKey = "lk_pub.secret-xyz" });
        service.ThrowListAfter = 1; // 1er chargement OK (initial), 2e (rafraîchissement post-action) échoue.
        Use(service, canManage: true);

        var cut = Render<Agents>();
        cut.Find("[data-testid='agents-register-btn']").Click();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='agent-register-name']").Should().ContainSingle());
        cut.Find("[data-testid='agent-register-name']").Input("Poste comptable");
        cut.Find("[data-testid='agent-register-submit']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='agent-register-key']").TextContent.Should().Contain("lk_pub.secret-xyz");
            cut.FindAll("[data-testid='agents-error']").Should().BeEmpty(
                "un échec de rafraîchissement post-action ne masque pas la page ni la clé unique");
        });
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

        public int ListCalls { get; private set; }

        /// <summary>Fait échouer ListAsync à partir du (ThrowListAfter+1)-ème appel (défaut : jamais).</summary>
        public int ThrowListAfter { get; set; } = int.MaxValue;

        public int RegisterCalls { get; private set; }

        public string? LastRegisterName { get; private set; }

        public int RevokeCalls { get; private set; }

        public int RotateCalls { get; private set; }

        public AgentKeyIssuedResult RegisterResult { get; set; } = new(
            AgentActionStatus.Succeeded,
            new AgentKeyIssuedDto { AgentId = Guid.NewGuid(), KeyPrefix = "lk", FullKey = "lk.secret" });

        public AgentActionStatus RevokeResult { get; set; } = AgentActionStatus.Succeeded;

        public AgentKeyIssuedResult RotateResult { get; set; } = new(
            AgentActionStatus.Succeeded,
            new AgentKeyIssuedDto { AgentId = Guid.NewGuid(), KeyPrefix = "lk2", FullKey = "lk2.secret" });

        public static FakeAgentManagementService Returning(params AgentConsoleLine[] agents) => new(agents, throws: false);

        public static FakeAgentManagementService Throwing() => new(null, throws: true);

        public Task<IReadOnlyList<AgentConsoleLine>> ListAsync(CancellationToken cancellationToken = default)
        {
            ListCalls++;
            if (_throws || ListCalls > ThrowListAfter)
            {
                throw new InvalidOperationException("Échec simulé de chargement du parc d'agents.");
            }

            return Task.FromResult(_agents!);
        }

        public Task<AgentKeyIssuedResult> RegisterAsync(string? name, CancellationToken cancellationToken = default)
        {
            RegisterCalls++;
            LastRegisterName = name;
            return Task.FromResult(RegisterResult);
        }

        public Task<AgentActionStatus> RevokeAsync(Guid agentId, CancellationToken cancellationToken = default)
        {
            RevokeCalls++;
            return Task.FromResult(RevokeResult);
        }

        public Task<AgentKeyIssuedResult> RotateKeyAsync(Guid agentId, CancellationToken cancellationToken = default)
        {
            RotateCalls++;
            return Task.FromResult(RotateResult);
        }
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
