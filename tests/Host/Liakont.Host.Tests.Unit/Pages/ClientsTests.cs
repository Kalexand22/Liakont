namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.Clients;
using Liakont.Host.Components.Pages;
using Liakont.Host.Security.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Stratum.Common.Abstractions.Grid;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.UI;
using Xunit;

/// <summary>
/// Écran « Clients » (OPS03 lot C) : rendu de la liste (badges de statut, SIREN « — », ligne
/// illisible VISIBLE), bouton « Nouveau client », dialogue de suspension (confirmation → action →
/// retour opérateur français ; client sans profil → message au lieu du dialogue), échec de
/// chargement visible — jamais silencieux.
/// </summary>
public sealed class ClientsTests : BunitContext
{
    private readonly FakeClientConsoleService _service = new();

    public ClientsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddCommonUI();
        Services.AddSingleton<IStringLocalizer<SharedResources>>(new StubLocalizer());
        Services.AddScoped<IActorContextAccessor>(_ => new StubActor());
        Services.AddScoped<IGridPreferenceService>(_ => new NullGridPrefs());
        Services.AddScoped<ISavedFilterService>(_ => new NullSavedFilters());
        Services.AddScoped<IClientConsoleService>(_ => _service);
    }

    private static ClientConsoleLine Line(
        string tenantId,
        ClientStatut statut = ClientStatut.Actif,
        string? siren = "123456782",
        bool readFailed = false) => new()
        {
            TenantId = tenantId,
            DisplayName = $"Client {tenantId}",
            Siren = siren,
            Statut = statut,
            AgentCount = 1,
            ProvisionedAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            ReadFailed = readFailed,
        };

    [Fact]
    public void Should_Render_The_Clients_With_Status_Badges_And_New_Client_Button()
    {
        _service.Lines = [Line("acme"), Line("beta", ClientStatut.Suspendu)];

        var cut = Render<Clients>();

        cut.Markup.Should().Contain("Client acme").And.Contain("Client beta");
        cut.FindAll("[data-testid='client-statut-actif']").Should().ContainSingle();
        cut.FindAll("[data-testid='client-statut-suspendu']").Should().ContainSingle();

        // StratumButton(Href) ne rend pas un <a> : on vérifie le BOUTON de création du gabarit
        // (testid {TestId}-create-btn) — la navigation réelle passe par NavigationManager.
        cut.FindAll("[data-testid='clients-create-btn']").Should().NotBeEmpty("le bouton « Nouveau client » mène à l'assistant");
    }

    [Fact]
    public void A_Client_Without_Profile_Shows_A_Dash_And_Its_Real_State()
    {
        _service.Lines = [Line("frais", ClientStatut.ProfilNonCree, siren: null)];

        var cut = Render<Clients>();

        cut.FindAll("[data-testid='client-statut-profil-non-cree']").Should().ContainSingle();
        cut.Markup.Should().Contain("—", "un SIREN absent s'affiche en tiret explicite");
    }

    [Fact]
    public void An_Unreadable_Client_Stays_Visible_With_Its_Failure_Flagged()
    {
        _service.Lines = [Line("kaput", ClientStatut.ProfilNonCree, siren: null, readFailed: true)];

        var cut = Render<Clients>();

        cut.Markup.Should().Contain("Client kaput", "la ligne reste VISIBLE");
        cut.FindAll("[data-testid='client-statut-read-failed']").Should().ContainSingle("l'échec de lecture est signalé");
    }

    [Fact]
    public void A_Load_Failure_Is_Visible_And_Hides_The_List()
    {
        _service.ThrowOnList = true;

        var cut = Render<Clients>();

        cut.FindAll("[data-testid='clients-error']").Should().ContainSingle();
        cut.FindAll("[data-testid='clients']").Should().BeEmpty();
    }

    [Fact]
    public void Confirming_The_Suspension_Calls_The_Service_And_Shows_The_French_Feedback()
    {
        _service.Lines = [Line("acme")];
        var cut = Render<Clients>();

        // Ouvre le dialogue par l'action de ligne, puis confirme.
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='quick-action-toggle-status']").Should().NotBeEmpty());
        cut.FindAll("[data-testid='quick-action-toggle-status']")[0].Click();
        cut.Find("[data-testid='client-status-dialog-confirm']").Click();

        _service.StatusCalls.Should().ContainSingle().Which.Should().Be(("acme", true));
        cut.Find("[data-testid='clients-action-feedback']").TextContent
            .Should().Contain("suspendu").And.Contain("intactes");
    }

    [Fact]
    public void A_Client_Without_Profile_Cannot_Be_Suspended_And_The_Reason_Is_Said()
    {
        _service.Lines = [Line("frais", ClientStatut.ProfilNonCree, siren: null)];
        var cut = Render<Clients>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='quick-action-toggle-status']").Should().NotBeEmpty());
        cut.FindAll("[data-testid='quick-action-toggle-status']")[0].Click();

        cut.FindAll("[data-testid='client-status-dialog']").Should().BeEmpty("pas de dialogue pour un client sans profil");
        cut.Find("[data-testid='clients-action-feedback']").TextContent.Should().Contain("n'a pas encore de profil");
        _service.StatusCalls.Should().BeEmpty();
    }

    private sealed class FakeClientConsoleService : IClientConsoleService
    {
        public IReadOnlyList<ClientConsoleLine> Lines { get; set; } = [];

        public bool ThrowOnList { get; set; }

        public List<(string TenantId, bool Suspendre)> StatusCalls { get; } = [];

        public Task<IReadOnlyList<ClientConsoleLine>> ListAsync(CancellationToken cancellationToken = default) =>
            ThrowOnList ? throw new InvalidOperationException("boom") : Task.FromResult(Lines);

        public IReadOnlyList<string> ListSeedDirectories() => [];

        public Task<ClientCreationResult> CreateTenantAsync(string tenantId, string displayName, string adminEmail, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ClientSeedResult> ImportSeedAsync(string tenantId, string seedDirectoryName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ClientActionResult> SaveProfileAsync(string tenantId, ClientProfileInput profile, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TenantUserProvisionResult> ProvisionFirstUserAsync(TenantUserProvisionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ClientAgentKeyResult> RegisterFirstAgentAsync(string tenantId, string agentName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ClientActionResult> SetStatusAsync(string tenantId, bool suspendre, CancellationToken cancellationToken = default)
        {
            StatusCalls.Add((tenantId, suspendre));
            return Task.FromResult(new ClientActionResult(ClientActionStatus.Succeeded));
        }
    }

    private sealed class StubLocalizer : IStringLocalizer<SharedResources>
    {
        public LocalizedString this[string name] => new(name, name, resourceNotFound: true);

        public LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(System.Globalization.CultureInfo.InvariantCulture, name, arguments), resourceNotFound: true);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    private sealed class StubActor : IActorContextAccessor
    {
        public IActorContext Current { get; } = new Ctx();

        private sealed class Ctx : IActorContext
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

    private sealed class NullGridPrefs : IGridPreferenceService
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

    private sealed class NullSavedFilters : ISavedFilterService
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
}
