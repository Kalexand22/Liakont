namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components.Pages;
using Liakont.Host.PaAccounts;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.UI;
using Xunit;

/// <summary>
/// Tests bUnit de la PAGE « Comptes plateforme agréée » (FIX01c) : garde liakont.settings (page de
/// secrets → accès refusé sans permission), échec de chargement visible, et parcours création / édition /
/// désactivation câblés vers le service (la clé saisie n'est jamais réaffichée). Le conflit (doublon
/// plug-in/environnement) remonte un message opérateur français dans l'éditeur, qui reste ouvert.
/// </summary>
public sealed class ComptesPaTests : BunitContext
{
    public ComptesPaTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddLocalization();
        Services.AddCommonUI();
        Services.AddSingleton<IActorContextAccessor>(new FakeActorContextAccessor());

        // Service de publication (FIX201) injecté par la page : un défaut bénin (aucun compte actif) suffit
        // aux tests existants ; les tests dédiés en réenregistrent un (le dernier enregistrement gagne).
        Services.AddScoped<IPaPublicationConsoleService>(_ => new FakePublicationService());
    }

    [Fact]
    public void Without_settings_permission_access_is_denied()
    {
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: false));
        Services.AddScoped<IPaAccountConsoleService>(_ => new FakePaService());

        var cut = Render<ComptesPa>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='comptes-pa-denied']").Should().ContainSingle());
        cut.FindAll("[data-testid='comptes-pa']").Should().BeEmpty();
    }

    [Fact]
    public void Load_failure_shows_a_visible_error_banner()
    {
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: true));
        Services.AddScoped<IPaAccountConsoleService>(_ => FakePaService.Throwing());

        var cut = Render<ComptesPa>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='comptes-pa-error']").Should().ContainSingle());
        cut.FindAll("[data-testid='comptes-pa']").Should().BeEmpty();
    }

    [Fact]
    public void With_permission_the_accounts_and_add_button_are_rendered()
    {
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: true));
        Services.AddScoped<IPaAccountConsoleService>(_ => new FakePaService(accounts: [Account()], pluginTypes: ["Fake"]));

        var cut = Render<ComptesPa>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='comptes-pa-account']").Should().ContainSingle());
        cut.FindAll("[data-testid='comptes-pa-add-btn']").Should().ContainSingle();
    }

    [Fact]
    public void Creating_a_pa_account_calls_create_and_closes_the_editor()
    {
        var fake = new FakePaService(accounts: [], pluginTypes: ["Fake"]);
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: true));
        Services.AddScoped<IPaAccountConsoleService>(_ => fake);

        var cut = Render<ComptesPa>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='comptes-pa-add-btn']").Should().ContainSingle());
        cut.Find("[data-testid='comptes-pa-add-btn']").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='comptes-pa-editor']").Should().ContainSingle());
        cut.Find("[data-testid='comptes-pa-plugintype']").Change("Fake");
        cut.Find("[data-testid='comptes-pa-environment']").Change("Staging");
        cut.Find("[data-testid='comptes-pa-apikey']").Input("a-secret-key");
        cut.Find("[data-testid='comptes-pa-save-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            fake.CreateCalls.Should().Be(1);
            cut.FindAll("[data-testid='comptes-pa-editor']").Should().BeEmpty();
        });
        fake.LastCreatedKey.Should().Be("a-secret-key", "la clé saisie est transmise au service (qui délègue le chiffrement)");
    }

    [Fact]
    public void Editing_a_pa_account_calls_update()
    {
        var account = Account();
        var fake = new FakePaService(accounts: [account], pluginTypes: ["Fake"]);
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: true));
        Services.AddScoped<IPaAccountConsoleService>(_ => fake);

        var cut = Render<ComptesPa>();

        cut.WaitForAssertion(() => cut.FindAll($"[data-testid='comptes-pa-edit-{account.Id}']").Should().ContainSingle());
        cut.Find($"[data-testid='comptes-pa-edit-{account.Id}']").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='comptes-pa-editor']").Should().ContainSingle());
        cut.Find("[data-testid='comptes-pa-save-btn']").Click();

        cut.WaitForAssertion(() => fake.UpdateCalls.Should().Be(1));
    }

    [Fact]
    public void Deactivating_a_pa_account_confirms_then_calls_deactivate()
    {
        var account = Account();
        var fake = new FakePaService(accounts: [account], pluginTypes: ["Fake"]);
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: true));
        Services.AddScoped<IPaAccountConsoleService>(_ => fake);

        var cut = Render<ComptesPa>();

        cut.WaitForAssertion(() => cut.FindAll($"[data-testid='comptes-pa-deactivate-{account.Id}']").Should().ContainSingle());
        cut.Find($"[data-testid='comptes-pa-deactivate-{account.Id}']").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='comptes-pa-deactivate-confirm']").Should().ContainSingle());
        cut.Find("[data-testid='comptes-pa-deactivate-confirm-btn']").Click();

        cut.WaitForAssertion(() => fake.DeactivateCalls.Should().Be(1));
    }

    [Fact]
    public void Conflict_on_create_shows_a_french_error_and_keeps_the_editor_open()
    {
        var fake = new FakePaService(accounts: [], pluginTypes: ["Fake"]) { ThrowConflictOnCreate = true };
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: true));
        Services.AddScoped<IPaAccountConsoleService>(_ => fake);

        var cut = Render<ComptesPa>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='comptes-pa-add-btn']").Should().ContainSingle());
        cut.Find("[data-testid='comptes-pa-add-btn']").Click();
        cut.Find("[data-testid='comptes-pa-plugintype']").Change("Fake");
        cut.Find("[data-testid='comptes-pa-environment']").Change("Staging");
        cut.Find("[data-testid='comptes-pa-save-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='comptes-pa-editor-error']").TextContent.Should().Contain("existe déjà");
            cut.FindAll("[data-testid='comptes-pa-editor']").Should().ContainSingle();
        });
    }

    [Fact]
    public void NotFound_on_edit_shows_a_french_error_and_keeps_the_editor_open()
    {
        var account = Account();
        var fake = new FakePaService(accounts: [account], pluginTypes: ["Fake"]) { ThrowNotFoundOnUpdate = true };
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: true));
        Services.AddScoped<IPaAccountConsoleService>(_ => fake);

        var cut = Render<ComptesPa>();

        cut.WaitForAssertion(() => cut.FindAll($"[data-testid='comptes-pa-edit-{account.Id}']").Should().ContainSingle());
        cut.Find($"[data-testid='comptes-pa-edit-{account.Id}']").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='comptes-pa-editor']").Should().ContainSingle());
        cut.Find("[data-testid='comptes-pa-save-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='comptes-pa-editor-error']").TextContent.Should().Contain("introuvable");
            cut.FindAll("[data-testid='comptes-pa-editor']").Should().ContainSingle();
        });
    }

    [Fact]
    public void NotFound_on_deactivate_shows_a_french_error_and_keeps_the_confirmation_open()
    {
        var account = Account();
        var fake = new FakePaService(accounts: [account], pluginTypes: ["Fake"]) { ThrowNotFoundOnDeactivate = true };
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: true));
        Services.AddScoped<IPaAccountConsoleService>(_ => fake);

        var cut = Render<ComptesPa>();

        cut.WaitForAssertion(() => cut.FindAll($"[data-testid='comptes-pa-deactivate-{account.Id}']").Should().ContainSingle());
        cut.Find($"[data-testid='comptes-pa-deactivate-{account.Id}']").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='comptes-pa-deactivate-confirm']").Should().ContainSingle());
        cut.Find("[data-testid='comptes-pa-deactivate-confirm-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='comptes-pa-deactivate-error']").TextContent.Should().Contain("introuvable");
            cut.FindAll("[data-testid='comptes-pa-deactivate-confirm']").Should().ContainSingle();
        });
    }

    [Fact]
    public void Publication_panel_renders_the_publish_action_for_an_active_account()
    {
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: true));
        Services.AddScoped<IPaAccountConsoleService>(_ => new FakePaService(accounts: [Account()], pluginTypes: ["Fake"]));
        Services.AddScoped<IPaPublicationConsoleService>(_ => new FakePublicationService(new PaPublicationState
        {
            HasActiveAccount = true,
            PluginType = "Fake",
            Environment = "Staging",
            StateAvailable = true,
            IsPublished = false,
            Siren = "123456782",
        }));

        var cut = Render<ComptesPa>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='pa-publication-open-btn']").Should().ContainSingle());
        cut.FindAll("[data-testid='pa-publication-unpublished']").Should().ContainSingle();
    }

    [Fact]
    public void Publishing_calls_the_service_and_shows_a_success_message()
    {
        var publication = new FakePublicationService(new PaPublicationState
        {
            HasActiveAccount = true,
            PluginType = "Fake",
            Environment = "Staging",
            StateAvailable = true,
            IsPublished = false,
            Siren = "123456782",
        })
        {
            ResultToReturn = PaPublicationResult.Ok("SIREN publié : la transmission est active depuis le 01/01/2026."),
        };
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(hasSettings: true));
        Services.AddScoped<IPaAccountConsoleService>(_ => new FakePaService(accounts: [Account()], pluginTypes: ["Fake"]));
        Services.AddScoped<IPaPublicationConsoleService>(_ => publication);

        var cut = Render<ComptesPa>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='pa-publication-open-btn']").Should().ContainSingle());
        cut.Find("[data-testid='pa-publication-open-btn']").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='pa-publication-form']").Should().ContainSingle());
        cut.Find("[data-testid='pa-publication-typeoperation']").Input("LBS");
        cut.Find("[data-testid='pa-publication-enterprisesize']").Input("PME");
        cut.Find("[data-testid='pa-publication-submit-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            publication.PublishCalls.Should().Be(1);
            cut.Find("[data-testid='pa-publication-success']").TextContent.Should().Contain("SIREN publié");
        });
    }

    private static PaAccountDto Account() => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Guid.NewGuid(),
        PluginType = "Fake",
        Environment = "Staging",
        AccountIdentifiers = "{}",
        HasApiKey = false,
        IsActive = true,
        CreatedAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private sealed class FakePaService : IPaAccountConsoleService
    {
        private readonly bool _throwOnLoad;
        private readonly IReadOnlyList<PaAccountDto> _accounts;
        private readonly IReadOnlyList<string> _pluginTypes;

        public FakePaService(IReadOnlyList<PaAccountDto>? accounts = null, IReadOnlyList<string>? pluginTypes = null, bool throwOnLoad = false)
        {
            _accounts = accounts ?? Array.Empty<PaAccountDto>();
            _pluginTypes = pluginTypes ?? Array.Empty<string>();
            _throwOnLoad = throwOnLoad;
        }

        public bool ThrowConflictOnCreate { get; init; }

        public bool ThrowNotFoundOnUpdate { get; init; }

        public bool ThrowNotFoundOnDeactivate { get; init; }

        public int CreateCalls { get; private set; }

        public int UpdateCalls { get; private set; }

        public int DeactivateCalls { get; private set; }

        public string? LastCreatedKey { get; private set; }

        public static FakePaService Throwing() => new(throwOnLoad: true);

        public Task<PaAccountConsoleModel> GetModelAsync(CancellationToken cancellationToken = default)
        {
            if (_throwOnLoad)
            {
                throw new InvalidOperationException("Échec simulé du chargement des comptes PA.");
            }

            return Task.FromResult(new PaAccountConsoleModel
            {
                Accounts = _accounts.Select(a => new PaAccountSettingsDto { Account = a, PluginAvailable = false, Capabilities = null }).ToList(),
                RegisteredPluginTypes = _pluginTypes,
            });
        }

        public Task<Guid> CreateAsync(PaAccountFormModel model, CancellationToken cancellationToken = default)
        {
            if (ThrowConflictOnCreate)
            {
                throw new ConflictException("Doublon (plug-in, environnement).");
            }

            CreateCalls++;
            LastCreatedKey = model.ApiKey;
            return Task.FromResult(Guid.NewGuid());
        }

        public Task UpdateAsync(PaAccountFormModel model, CancellationToken cancellationToken = default)
        {
            if (ThrowNotFoundOnUpdate)
            {
                throw new NotFoundException("PaAccount", model.PaAccountId ?? Guid.Empty);
            }

            UpdateCalls++;
            return Task.CompletedTask;
        }

        public Task DeactivateAsync(Guid paAccountId, CancellationToken cancellationToken = default)
        {
            if (ThrowNotFoundOnDeactivate)
            {
                throw new NotFoundException("PaAccount", paAccountId);
            }

            DeactivateCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePublicationService : IPaPublicationConsoleService
    {
        private readonly PaPublicationState _state;

        public FakePublicationService(PaPublicationState? state = null) =>
            _state = state ?? new PaPublicationState { HasActiveAccount = false, StateAvailable = false };

        public PaPublicationResult ResultToReturn { get; init; } = PaPublicationResult.Ok("SIREN publié.");

        public int PublishCalls { get; private set; }

        public Task<PaPublicationState> GetStateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_state);

        public Task<PaPublicationResult> PublishAsync(PaPublicationFormModel form, CancellationToken cancellationToken = default)
        {
            PublishCalls++;
            return Task.FromResult(ResultToReturn);
        }
    }

    private sealed class FakePermissionService : IPermissionService
    {
        private readonly bool _hasSettings;

        public FakePermissionService(bool hasSettings) => _hasSettings = hasSettings;

        public event Action? OnPermissionsChanged
        {
            add { }
            remove { }
        }

        public bool HasPermission(string permission) =>
            _hasSettings && string.Equals(permission, "liakont.settings", StringComparison.Ordinal);
    }

    private sealed class FakeActorContextAccessor : IActorContextAccessor
    {
        public IActorContext Current { get; } = new AnonymousActorContext();

        private sealed class AnonymousActorContext : IActorContext
        {
            public Guid UserId => Guid.Empty;

            public Guid CorrelationId { get; } = Guid.NewGuid();

            public bool IsAuthenticated => false;

            public string? DisplayName => null;

            public string? Email => null;

            public Guid? CompanyId => null;

            public string? Timezone => null;

            public string? Language => null;

            public string? TenantId => null;
        }
    }
}
