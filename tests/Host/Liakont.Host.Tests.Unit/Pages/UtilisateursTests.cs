namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components.Pages;
using Liakont.Host.Security.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Stratum.Common.Abstractions.Grid;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.UI;
using Xunit;

/// <summary>
/// Écran « Utilisateurs » d'un client (RB4 inc1) : rendu de la liste (identifiant, rôles, statut),
/// état vide, échec de chargement VISIBLE, création (appelle le provisioning et remet le mot de passe
/// temporaire une fois), réinitialisation (appelle le service de gestion et remet le mot de passe).
/// </summary>
public sealed class UtilisateursTests : BunitContext
{
    private readonly FakeUserManagement _management = new();
    private readonly FakeUserProvisioning _provisioning = new();

    public UtilisateursTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddCommonUI();
        Services.AddSingleton<IStringLocalizer<SharedResources>>(new StubLocalizer());
        Services.AddScoped<IActorContextAccessor>(_ => new StubActor());
        Services.AddScoped<IGridPreferenceService>(_ => new NullGridPrefs());
        Services.AddScoped<ISavedFilterService>(_ => new NullSavedFilters());
        Services.AddScoped<ITenantUserManagementService>(_ => _management);
        Services.AddScoped<ITenantUserProvisioningService>(_ => _provisioning);
        Services.AddScoped<ITenantQueries>(_ => new FakeTenantQueries());
    }

    private static TenantUserLine User(string username, params string[] roles) => new()
    {
        IdpUserId = $"idp-{username}",
        Username = username,
        Email = $"{username}@client.fr",
        DisplayName = username,
        Enabled = true,
        Roles = roles,
    };

    private IRenderedComponent<Utilisateurs> RenderPage() =>
        Render<Utilisateurs>(p => p.Add(c => c.TenantId, "acme"));

    [Fact]
    public void Should_Render_The_Users_With_Roles_And_The_Create_Button()
    {
        _management.Lines = [User("lecture", "lecture"), User("operateur", "lecture", "operateur")];

        var cut = RenderPage();

        cut.Markup.Should().Contain("lecture").And.Contain("operateur");
        cut.FindAll("[data-testid='users-create-btn']").Should().ContainSingle();
    }

    [Fact]
    public void Should_Show_Empty_When_No_Users()
    {
        _management.Lines = [];

        var cut = RenderPage();

        cut.FindAll("[data-testid='users-empty']").Should().ContainSingle();
    }

    [Fact]
    public void A_Load_Failure_Is_Visible_And_Hides_The_List()
    {
        _management.ThrowOnList = true;

        var cut = RenderPage();

        cut.FindAll("[data-testid='users-error']").Should().ContainSingle();
        cut.FindAll("[data-testid='users']").Should().BeEmpty();
    }

    [Fact]
    public void Creating_A_User_Calls_Provisioning_And_Shows_The_Temporary_Password_Once()
    {
        _management.Lines = [];
        _provisioning.Result = new TenantUserProvisionResult { Success = true, TemporaryPassword = "TEMP-pass-123" };
        var cut = RenderPage();

        cut.Find("[data-testid='users-create-btn']").Click();
        cut.Find("[data-testid='users-create-username']").Change("j.durand");
        cut.Find("[data-testid='users-create-displayname']").Change("Jean Durand");
        cut.Find("[data-testid='users-create-email']").Change("jean.durand@client.fr");
        cut.Find("[data-testid='users-create-submit']").Click();

        _provisioning.Calls.Should().ContainSingle();
        _provisioning.Calls[0].Username.Should().Be("j.durand");
        _provisioning.Calls[0].TenantId.Should().Be("acme");
        cut.Find("[data-testid='users-secret-value']").TextContent.Should().Contain("TEMP-pass-123");
    }

    [Fact]
    public void Resetting_A_Password_Calls_The_Service_And_Shows_The_Temporary_Password()
    {
        _management.Lines = [User("lecture", "lecture")];
        _management.ResetResult = new TenantUserPasswordResetResult { Success = true, TemporaryPassword = "RESET-pass-456" };
        var cut = RenderPage();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='quick-action-reset-password']").Should().NotBeEmpty());
        cut.FindAll("[data-testid='quick-action-reset-password']")[0].Click();
        cut.Find("[data-testid='users-reset-submit']").Click();

        _management.ResetCalls.Should().ContainSingle().Which.Should().Be(("acme", "idp-lecture"));
        cut.Find("[data-testid='users-secret-value']").TextContent.Should().Contain("RESET-pass-456");
    }

    private sealed class FakeUserManagement : ITenantUserManagementService
    {
        public IReadOnlyList<TenantUserLine> Lines { get; set; } = [];

        public bool ThrowOnList { get; set; }

        public List<(string TenantId, string IdpUserId)> ResetCalls { get; } = [];

        public TenantUserPasswordResetResult ResetResult { get; set; } =
            new() { Success = true, TemporaryPassword = "tmp" };

        public Task<IReadOnlyList<TenantUserLine>> ListUsersAsync(string tenantId, CancellationToken cancellationToken = default) =>
            ThrowOnList ? throw new InvalidOperationException("boom") : Task.FromResult(Lines);

        public Task<TenantUserPasswordResetResult> ResetPasswordAsync(string tenantId, string idpUserId, CancellationToken cancellationToken = default)
        {
            ResetCalls.Add((tenantId, idpUserId));
            return Task.FromResult(ResetResult);
        }
    }

    private sealed class FakeUserProvisioning : ITenantUserProvisioningService
    {
        public List<TenantUserProvisionRequest> Calls { get; } = [];

        public TenantUserProvisionResult Result { get; set; } =
            new() { Success = true, TemporaryPassword = "tmp" };

        public Task<TenantUserProvisionResult> ProvisionUserAsync(TenantUserProvisionRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add(request);
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeTenantQueries : ITenantQueries
    {
        public Task<IReadOnlyList<TenantDto>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TenantDto>>([]);

        public Task<TenantDto?> GetByIdAsync(string tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<TenantDto?>(new TenantDto
            {
                Id = tenantId,
                DisplayName = "Client " + tenantId,
                AdminEmail = "admin@client.fr",
                DatabaseName = "db",
                IsActive = true,
                ProvisionedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            });
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

            public string? TenantId => "acme";
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
