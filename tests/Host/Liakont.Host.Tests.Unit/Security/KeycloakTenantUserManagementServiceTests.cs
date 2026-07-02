namespace Liakont.Host.Tests.Unit.Security;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Notifications;
using Liakont.Host.Security.Keycloak;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.Keycloak;
using Stratum.Modules.Notification.Contracts;
using Xunit;

/// <summary>
/// Réinitialisation du mot de passe d'un utilisateur de tenant (RB4) — chemin email (BUG-31) : le mot
/// de passe temporaire est DÉJÀ posé chez l'IdP quand l'email de réinitialisation est tenté ; quel que
/// soit le sort de l'envoi (pas de config, échec de la SONDE de disponibilité — elle touche la base
/// système —, échec d'envoi), le reset RÉUSSIT et le mot de passe est remis UNE fois à l'opérateur —
/// jamais un 500 qui le perdrait. Le Keycloak admin (token + GET user) est servi par un handler HTTP
/// factice ; la sonde réelle (config en base OU appsettings) est testée sur <c>SmtpEmailTransport</c>.
/// </summary>
public sealed class KeycloakTenantUserManagementServiceTests
{
    private const string SharedRealm = "liakont-dev";
    private const string IdpUserId = "kc-user-1";
    private static readonly Guid ProfileCompanyId = Guid.Parse("22222222-2222-4222-a222-222222222222");

    private readonly RecordingKeycloakProvisioner _keycloak = new();
    private readonly RecordingEmailTransport _emailTransport = new();

    private bool _emailConfigured;
    private Exception? _probeFailure;

    [Fact]
    public async Task Without_Email_Configured_The_Temporary_Password_Is_Returned_Once()
    {
        var result = await CreateSut().ResetPasswordAsync("acme", IdpUserId);

        result.Success.Should().BeTrue();
        result.InvitationEmailSent.Should().BeFalse();
        result.TemporaryPassword.Should().NotBeNullOrWhiteSpace("sans envoi possible, l'opérateur reçoit le mot de passe UNE fois");
        result.TemporaryPassword.Should().Be(_keycloak.LastResetPassword, "le mot de passe remis est celui posé chez l'IdP");
        _keycloak.LastResetTemporary.Should().BeTrue("changement forcé au prochain login");
        _emailTransport.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task With_Email_Configured_The_Reset_Email_Is_Sent_And_The_Password_Never_Returned()
    {
        _emailConfigured = true;

        var result = await CreateSut().ResetPasswordAsync("acme", IdpUserId);

        result.Success.Should().BeTrue();
        result.InvitationEmailSent.Should().BeTrue();
        result.TemporaryPassword.Should().BeNull("le mot de passe ne sort QUE par l'email — jamais restitué en plus");

        var email = _emailTransport.Sent.Should().ContainSingle().Subject;
        email.Recipient.Should().Be("j.dupont@exemple.test");
        email.Body.Should().Contain(_keycloak.LastResetPassword, "l'email porte le mot de passe temporaire");
    }

    [Fact]
    public async Task When_The_Availability_Probe_Fails_The_Temporary_Password_Is_Returned_Once()
    {
        // La sonde de disponibilité (BUG-31) touche la base système : son échec vaut « pas d'envoi
        // possible » — même repli que l'échec d'envoi. Le mot de passe est DÉJÀ posé chez l'IdP :
        // un 500 ici le perdrait définitivement (utilisateur bloqué).
        _emailConfigured = true;
        _probeFailure = new InvalidOperationException("base système indisponible");

        var result = await CreateSut().ResetPasswordAsync("acme", IdpUserId);

        result.Success.Should().BeTrue("un problème de config email n'avorte pas le reset");
        result.InvitationEmailSent.Should().BeFalse();
        result.TemporaryPassword.Should().Be(_keycloak.LastResetPassword, "le mot de passe est remis UNE fois à l'opérateur");
        _emailTransport.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task When_The_Email_Send_Fails_The_Temporary_Password_Is_Returned_Instead()
    {
        _emailConfigured = true;
        _emailTransport.ThrowOnSend = new InvalidOperationException("SMTP en panne");

        var result = await CreateSut().ResetPasswordAsync("acme", IdpUserId);

        result.Success.Should().BeTrue("l'échec d'envoi n'avorte pas le reset");
        result.InvitationEmailSent.Should().BeFalse();
        result.TemporaryPassword.Should().NotBeNullOrWhiteSpace();
    }

    private KeycloakTenantUserManagementService CreateSut()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{KeycloakAdminOptions.SectionName}:DedicatedRealmPerTenant"] = "false",
            })
            .Build();

        return new KeycloakTenantUserManagementService(
            new FakeTenantQueries(),
            new FakeTenantScopeFactory(),
            _keycloak,
            _emailTransport,
            new FakeEmailSendAvailability(_emailConfigured, _probeFailure),
            new FakeHttpClientFactory(new FakeKeycloakAdminHandler()),
            Options.Create(new KeycloakAdminOptions
            {
                AdminBaseUrl = "http://keycloak.test:8080",
                AdminPassword = "x",
                AppBaseUrl = "https://console.exemple.test",
                PrimaryRealmName = SharedRealm,
            }),
            configuration,
            NullLogger<KeycloakTenantUserManagementService>.Instance);
    }

    /// <summary>
    /// Keycloak admin factice : répond au POST de token ROPC et au GET de l'utilisateur du realm
    /// partagé (attribut <c>company_id</c> aligné sur le profil du tenant — garde tenant-scope).
    /// </summary>
    private sealed class FakeKeycloakAdminHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (request.Method == HttpMethod.Post && path.EndsWith("/protocol/openid-connect/token", StringComparison.Ordinal))
            {
                return Task.FromResult(Json("""{"access_token":"token-test"}"""));
            }

            if (request.Method == HttpMethod.Get && path.EndsWith($"/admin/realms/{SharedRealm}/users/{IdpUserId}", StringComparison.Ordinal))
            {
                return Task.FromResult(Json(
                    $$"""
                    {
                      "id": "{{IdpUserId}}",
                      "username": "jdupont",
                      "email": "j.dupont@exemple.test",
                      "enabled": true,
                      "attributes": { "company_id": ["{{ProfileCompanyId}}"] }
                    }
                    """));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(string payload) =>
            new(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class FakeEmailSendAvailability : IEmailSendAvailability
    {
        private readonly bool _configured;
        private readonly Exception? _throwOnProbe;

        public FakeEmailSendAvailability(bool configured, Exception? throwOnProbe)
        {
            _configured = configured;
            _throwOnProbe = throwOnProbe;
        }

        public Task<bool> IsConfiguredAsync(CancellationToken ct = default) =>
            _throwOnProbe is null ? Task.FromResult(_configured) : Task.FromException<bool>(_throwOnProbe);
    }

    /// <summary>Seul <c>ResetPasswordAsync</c> est attendu sur ce chemin — le reste lève.</summary>
    private sealed class RecordingKeycloakProvisioner : IKeycloakUserProvisioner
    {
        public string? LastResetPassword { get; private set; }

        public bool LastResetTemporary { get; private set; }

        public Task ResetPasswordAsync(string realmName, string userId, string password, bool temporary, CancellationToken cancellationToken = default)
        {
            LastResetPassword = password;
            LastResetTemporary = temporary;
            return Task.CompletedTask;
        }

        public Task<string?> FindUserIdByUsernameAsync(string realmName, string username, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string> CreateUserAsync(string realmName, KeycloakUserSpec spec, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetUserAttributesAsync(string realmName, string userId, IReadOnlyDictionary<string, string> attributes, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task EnsureRealmRoleAsync(string realmName, string roleName, string description, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AssignRealmRolesAsync(string realmName, string userId, IReadOnlyList<string> roleNames, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteUserAsync(string realmName, string userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingEmailTransport : IEmailTransport
    {
        public List<(string Recipient, string Subject, string Body)> Sent { get; } = [];

        public Exception? ThrowOnSend { get; set; }

        public Task SendAsync(string recipient, string subject, string body, CancellationToken ct = default)
        {
            if (ThrowOnSend is not null)
            {
                throw ThrowOnSend;
            }

            Sent.Add((recipient, subject, body));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTenantQueries : ITenantQueries
    {
        private static TenantDto Tenant() => new()
        {
            Id = "acme",
            DisplayName = "Acme",
            AdminEmail = "admin@acme.test",
            DatabaseName = "stratum_acme",
            RealmName = "stratum-acme",
            IsActive = true,
            ProvisionedAt = DateTimeOffset.UtcNow,
            CompanyId = ProfileCompanyId,
        };

        public Task<IReadOnlyList<TenantDto>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TenantDto>>([Tenant()]);

        public Task<TenantDto?> GetByIdAsync(string tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<TenantDto?>(Tenant());
    }

    /// <summary>Scope tenant factice : seul <c>ITenantSettingsQueries</c> est requis (résolution du company_id).</summary>
    private sealed class FakeTenantScopeFactory : ITenantScopeFactory
    {
        public ITenantScope Create(string tenantId)
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITenantSettingsQueries>(new FakeTenantSettingsQueries());
            return new FakeTenantScope(tenantId, services.BuildServiceProvider());
        }

        private sealed class FakeTenantScope : ITenantScope
        {
            private readonly ServiceProvider _provider;

            public FakeTenantScope(string tenantId, ServiceProvider provider)
            {
                TenantId = tenantId;
                _provider = provider;
            }

            public string TenantId { get; }

            public IServiceProvider Services => _provider;

            public ValueTask DisposeAsync() => _provider.DisposeAsync();
        }
    }

    private sealed class FakeTenantSettingsQueries : ITenantSettingsQueries
    {
        public Task<Guid?> GetCurrentCompanyId(CancellationToken ct = default) => Task.FromResult<Guid?>(ProfileCompanyId);

        public Task<string?> GetCurrentTenantStatut(CancellationToken ct = default) =>
            Task.FromResult<string?>(null);

        public Task<Liakont.Modules.TenantSettings.Contracts.DTOs.TenantProfileDto?> GetTenantProfile(Guid companyId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Liakont.Modules.TenantSettings.Contracts.DTOs.FiscalSettingsDto?> GetFiscalSettings(Guid companyId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Liakont.Modules.TenantSettings.Contracts.DTOs.BillingMentionsDto?> GetBillingMentions(Guid companyId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Liakont.Modules.TenantSettings.Contracts.DTOs.PaAccountDto>> GetPaAccounts(Guid companyId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Liakont.Modules.TenantSettings.Contracts.DTOs.ExtractionScheduleDto?> GetExtractionSchedule(Guid companyId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Liakont.Modules.TenantSettings.Contracts.DTOs.AlertThresholdsDto?> GetAlertThresholds(Guid companyId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> GetAuctionVerticalEnabled(Guid companyId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
