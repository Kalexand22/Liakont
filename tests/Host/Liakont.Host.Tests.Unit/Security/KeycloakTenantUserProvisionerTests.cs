namespace Liakont.Host.Tests.Unit.Security;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Notifications;
using Liakont.Host.Security;
using Liakont.Host.Security.Abstractions;
using Liakont.Host.Security.Keycloak;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.Keycloak;
using Stratum.Modules.Identity.Contracts.Commands;
using Stratum.Modules.Identity.Contracts.DTOs;
using Stratum.Modules.Identity.Contracts.Queries;
using Stratum.Modules.Notification.Contracts;
using Xunit;

/// <summary>
/// Provisioning d'un utilisateur de tenant (OPS03 lot A) : refus explicites (rôle inconnu, tenant
/// introuvable/désactivé, doublon username, conflit IdP username/email), résolution du company_id
/// (profil puis registre), invitation envoyée de façon SYNCHRONE quand le SMTP est configuré (mot
/// de passe JAMAIS restitué alors, JAMAIS persisté), mot de passe temporaire remis UNE fois sinon
/// (y compris sur échec d'envoi), compensation (suppression du compte IdP) quand la création du
/// compte applicatif échoue, et REPRISE idempotente (compte applicatif préexistant RELIÉ).
/// </summary>
public sealed class KeycloakTenantUserProvisionerTests
{
    private const string SharedRealm = "liakont-dev";
    private static readonly Guid RegistryCompanyId = Guid.Parse("11111111-1111-4111-a111-111111111111");
    private static readonly Guid ProfileCompanyId = Guid.Parse("22222222-2222-4222-a222-222222222222");

    private readonly FakeKeycloakUserProvisioner _keycloak = new();
    private readonly RecordingEmailTransport _emailTransport = new();
    private readonly RecordingSender _sender = new();

    private Guid? _profileCompanyId = ProfileCompanyId;
    private TenantDto? _tenant = Tenant();
    private UserDto? _existingAppUser;

    private static TenantDto Tenant(bool isActive = true, string? realm = "stratum-acme", bool noCompanyId = false) => new()
    {
        Id = "acme",
        DisplayName = "Acme",
        AdminEmail = "admin@acme.test",
        DatabaseName = "stratum_acme",
        RealmName = realm,
        IsActive = isActive,
        ProvisionedAt = DateTimeOffset.UtcNow,
        CompanyId = noCompanyId ? null : RegistryCompanyId,
    };

    private static TenantUserProvisionRequest Request(string role = "operateur") => new()
    {
        TenantId = "acme",
        Email = "j.dupont@exemple.test",
        Username = "jdupont",
        DisplayName = "Jeanne Dupont",
        Role = role,
    };

    [Fact]
    public async Task Should_Reject_Unknown_Role_With_The_Valid_Roles_Listed()
    {
        var result = await CreateSut().ProvisionUserAsync(Request(role: "patron"));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("patron").And.Contain("lecture").And.Contain("superviseur");
        _keycloak.CreatedUsers.Should().BeEmpty("aucune écriture IdP sur une demande invalide");
    }

    [Fact]
    public async Task Should_Reject_Unknown_Tenant()
    {
        _tenant = null;

        var result = await CreateSut().ProvisionUserAsync(Request());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("introuvable");
    }

    [Fact]
    public async Task Should_Reject_Deactivated_Tenant()
    {
        _tenant = Tenant(isActive: false);

        var result = await CreateSut().ProvisionUserAsync(Request());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("désactivé");
    }

    [Fact]
    public async Task Should_Reject_Duplicate_Username()
    {
        _keycloak.ExistingUsername = "jdupont";

        var result = await CreateSut().ProvisionUserAsync(Request());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("existe déjà");
        _keycloak.CreatedUsers.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_Smtp_The_Temporary_Password_Is_Returned_Once()
    {
        var result = await CreateSut(smtpConfigured: false).ProvisionUserAsync(Request());

        result.Success.Should().BeTrue();
        result.InvitationEmailSent.Should().BeFalse();
        result.TemporaryPassword.Should().NotBeNullOrWhiteSpace("sans SMTP, l'opérateur reçoit le mot de passe UNE fois");
        _emailTransport.Sent.Should().BeEmpty();

        // Le mot de passe remis est celui posé chez l'IdP, en mode temporaire (changement forcé).
        _keycloak.LastResetPassword.Should().Be(result.TemporaryPassword);
        _keycloak.LastResetTemporary.Should().BeTrue();
    }

    [Fact]
    public async Task With_Smtp_The_Invitation_Is_Sent_And_The_Password_Never_Returned()
    {
        var result = await CreateSut(smtpConfigured: true).ProvisionUserAsync(Request());

        result.Success.Should().BeTrue();
        result.InvitationEmailSent.Should().BeTrue();
        result.TemporaryPassword.Should().BeNull("le mot de passe ne sort QUE par l'invitation email — jamais persisté");

        var email = _emailTransport.Sent.Should().ContainSingle().Subject;
        email.Recipient.Should().Be("j.dupont@exemple.test");
        email.Body.Should().Contain(_keycloak.LastResetPassword, "l'invitation porte le mot de passe temporaire");
    }

    [Fact]
    public async Task When_The_Invitation_Send_Fails_The_Password_Is_Returned_Instead()
    {
        _emailTransport.ThrowOnSend = new InvalidOperationException("SMTP en panne");

        var result = await CreateSut(smtpConfigured: true).ProvisionUserAsync(Request());

        result.Success.Should().BeTrue("le compte est créé — l'échec d'envoi n'avorte pas le provisioning");
        result.InvitationEmailSent.Should().BeFalse();
        result.TemporaryPassword.Should().NotBeNullOrWhiteSpace("l'opérateur reçoit le mot de passe à l'écran, une fois");
    }

    [Fact]
    public async Task An_Idp_Conflict_On_Email_Is_A_Clean_French_Refusal()
    {
        // Le pré-contrôle ne couvre que le username : un email déjà pris dans le realm fait un 409
        // Keycloak → refus opérateur propre, jamais un 500.
        _keycloak.ThrowConflictOnCreate = true;

        var result = await CreateSut().ProvisionUserAsync(Request());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("existe déjà");
    }

    [Fact]
    public async Task A_Leftover_Application_Account_Is_Relinked_Instead_Of_Failing()
    {
        // Reprise après un échec antérieur (compensation IdP seule) : le compte applicatif du même
        // username est RELIÉ au nouveau compte IdP — jamais « définitivement non provisionnable ».
        var leftoverId = Guid.NewGuid();
        _existingAppUser = new UserDto
        {
            Id = leftoverId,
            Username = "jdupont",
            Email = "j.dupont@exemple.test",
            DisplayName = "Jeanne Dupont",
            IsActive = true,
            Roles = [],
        };

        var result = await CreateSut().ProvisionUserAsync(Request());

        result.Success.Should().BeTrue();
        result.UserId.Should().Be(leftoverId);
        var link = _sender.Sent.OfType<LinkExternalIdCommand>().Should().ContainSingle().Subject;
        link.UserId.Should().Be(leftoverId);
        link.ExternalId.Should().Be(result.IdpUserId);
        _sender.Sent.OfType<CreateUserCommand>().Should().BeEmpty("le compte existant est réutilisé, pas dupliqué");
    }

    [Fact]
    public async Task Should_Link_The_Idp_Account_To_The_Application_User()
    {
        var result = await CreateSut().ProvisionUserAsync(Request());

        result.Success.Should().BeTrue();
        result.IdpUserId.Should().Be(_keycloak.CreatedUsers.Single().Id);

        // CreateUserCommand dans le scope du tenant cible, ExternalId = sub IdP.
        var command = _sender.Sent.OfType<CreateUserCommand>().Should().ContainSingle().Subject;
        command.ExternalId.Should().Be(result.IdpUserId);

        // stratum_user_id = identity.users.id ; company_id posé aussi (realms anciens à mapper attribut).
        _keycloak.LastAttributes.Should().NotBeNull();
        _keycloak.LastAttributes!["stratum_user_id"].Should().Be(result.UserId!.Value.ToString());
        _keycloak.LastAttributes["company_id"].Should().Be(ProfileCompanyId.ToString(), "le profil du tenant fait foi");
    }

    [Fact]
    public async Task Without_Profile_The_Registry_CompanyId_Is_Used()
    {
        _profileCompanyId = null;

        var result = await CreateSut().ProvisionUserAsync(Request());

        result.Success.Should().BeTrue();
        _keycloak.LastAttributes!["company_id"].Should().Be(RegistryCompanyId.ToString());
    }

    [Fact]
    public async Task Without_Any_CompanyId_The_Provisioning_Is_Refused()
    {
        _profileCompanyId = null;
        _tenant = Tenant(noCompanyId: true);

        var result = await CreateSut().ProvisionUserAsync(Request());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("company_id");
        _keycloak.CreatedUsers.Should().BeEmpty("jamais de compte IdP sans scope de données");
    }

    [Fact]
    public async Task When_The_Application_User_Creation_Fails_The_Idp_Account_Is_Deleted()
    {
        _sender.ThrowOnSend = new InvalidOperationException("DB down");

        var act = () => CreateSut().ProvisionUserAsync(Request());

        await act.Should().ThrowAsync<InvalidOperationException>();
        _keycloak.DeletedUserIds.Should().ContainSingle("compensation : pas de compte IdP orphelin")
            .Which.Should().Be(_keycloak.CreatedUsers.Single().Id);
    }

    [Fact]
    public async Task The_Standard_Role_Is_Ensured_Then_Assigned()
    {
        await CreateSut().ProvisionUserAsync(Request(role: "lecture"));

        _keycloak.EnsuredRoles.Should().Contain("lecture");
        _keycloak.AssignedRoles.Should().ContainSingle().Which.Should().Be("lecture");
    }

    [Fact]
    public void The_Standard_Roles_Are_All_Known_To_The_Permission_Catalog()
    {
        // Anti-dérive : chaque rôle proposable au provisioning est projeté par la matrice §3
        // (aucun rôle inventé — un rôle sans permission serait un compte inutilisable).
        foreach (var role in LiakontRealmRoles.All)
        {
            RolePermissionCatalog.PermissionsForRoles([role])
                .Should().NotBeEmpty($"le rôle standard « {role} » doit exister dans la matrice §3");
        }
    }

    [Fact]
    public async Task In_Shared_Profile_The_User_Is_Created_In_The_Shared_Realm()
    {
        // Profil PARTAGÉ (défaut, ADR-0021 §1) : l'utilisateur va dans le realm partagé (PrimaryRealmName),
        // JAMAIS dans tenant.RealmName (= « stratum-acme », realm jamais provisionné → 404). Anti-régression
        // du bloqueur GATE_REALM_UNIQUE — le fake ignorait le realm, d'où le faux-vert d'origine.
        var result = await CreateSut().ProvisionUserAsync(Request());

        result.Success.Should().BeTrue();
        _keycloak.RealmsTouched.Should().NotBeEmpty();
        _keycloak.RealmsTouched.Should().OnlyContain(r => r == SharedRealm);
        _keycloak.RealmsTouched.Should().NotContain("stratum-acme", "le realm-par-tenant n'existe pas en profil partagé");
    }

    [Fact]
    public async Task In_Dedicated_Profile_The_User_Is_Created_In_The_Tenant_Realm()
    {
        // Déploiement DÉDIÉ mono-tenant (Keycloak:DedicatedRealmPerTenant=true) : le realm du tenant fait foi.
        var result = await CreateSut(dedicatedRealm: true).ProvisionUserAsync(Request());

        result.Success.Should().BeTrue();
        _keycloak.RealmsTouched.Should().NotBeEmpty();
        _keycloak.RealmsTouched.Should().OnlyContain(r => r == "stratum-acme");
    }

    [Fact]
    public async Task The_Provisioned_User_Is_Forced_To_Enroll_2FA_At_First_Login()
    {
        // RLM01 / gate ② : le 2FA est imposé au login mot de passe. Le user provisionné DOIT porter
        // CONFIGURE_TOTP — sinon (flow OTP Keycloak par défaut, CONDITIONNEL) il se connecterait sans
        // 2FA (finding F6 : le 2FA ne tenait qu'au CONFIGURE_TOTP seedé des comptes de démo).
        await CreateSut().ProvisionUserAsync(Request());

        var spec = _keycloak.CreatedUsers.Should().ContainSingle().Subject.Spec;
        spec.RequiredActions.Should().Contain("CONFIGURE_TOTP", "2FA forcé à la première connexion (RLM01)");
        spec.RequiredActions.Should().Contain("UPDATE_PASSWORD", "le mot de passe temporaire doit être changé");
    }

    private KeycloakTenantUserProvisioner CreateSut(bool smtpConfigured = false, bool dedicatedRealm = false)
    {
        var smtp = smtpConfigured
            ? new SmtpOptions { Enabled = true, Host = "smtp.exemple.test", FromAddress = "noreply@exemple.test" }
            : new SmtpOptions();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{KeycloakAdminOptions.SectionName}:DedicatedRealmPerTenant"] = dedicatedRealm ? "true" : "false",
            })
            .Build();

        return new KeycloakTenantUserProvisioner(
            new FakeTenantQueries(() => _tenant),
            new FakeTenantScopeFactory(_sender, () => _profileCompanyId, () => _existingAppUser),
            _keycloak,
            _emailTransport,
            new Microsoft.AspNetCore.Http.HttpContextAccessor(),
            Options.Create(smtp),
            Options.Create(new KeycloakAdminOptions
            {
                AdminBaseUrl = "http://localhost:8080",
                AdminPassword = "x",
                AppBaseUrl = "https://console.exemple.test",
                PrimaryRealmName = SharedRealm,
            }),
            configuration,
            NullLogger<KeycloakTenantUserProvisioner>.Instance);
    }

    private sealed class FakeKeycloakUserProvisioner : IKeycloakUserProvisioner
    {
        public string? ExistingUsername { get; set; }

        public bool ThrowConflictOnCreate { get; set; }

        public List<(string Id, KeycloakUserSpec Spec)> CreatedUsers { get; } = [];

        public List<string> DeletedUserIds { get; } = [];

        public List<string> EnsuredRoles { get; } = [];

        public List<string> AssignedRoles { get; } = [];

        public string? LastResetPassword { get; private set; }

        public bool LastResetTemporary { get; private set; }

        public IReadOnlyDictionary<string, string>? LastAttributes { get; private set; }

        /// <summary>Realm passé à CHAQUE appel IdP — permet d'asserter le realm cible (anti-faux-vert F2).</summary>
        public List<string> RealmsTouched { get; } = [];

        public Task<string?> FindUserIdByUsernameAsync(string realmName, string username, CancellationToken cancellationToken = default)
        {
            RealmsTouched.Add(realmName);
            return Task.FromResult(string.Equals(username, ExistingUsername, StringComparison.OrdinalIgnoreCase) ? "kc-existing" : null);
        }

        public Task<string> CreateUserAsync(string realmName, KeycloakUserSpec spec, CancellationToken cancellationToken = default)
        {
            RealmsTouched.Add(realmName);
            if (ThrowConflictOnCreate)
            {
                throw new KeycloakUserConflictException("email already exists");
            }

            var id = $"kc-{CreatedUsers.Count + 1}";
            CreatedUsers.Add((id, spec));
            return Task.FromResult(id);
        }

        public Task SetUserAttributesAsync(string realmName, string userId, IReadOnlyDictionary<string, string> attributes, CancellationToken cancellationToken = default)
        {
            RealmsTouched.Add(realmName);
            LastAttributes = attributes;
            return Task.CompletedTask;
        }

        public Task ResetPasswordAsync(string realmName, string userId, string password, bool temporary, CancellationToken cancellationToken = default)
        {
            RealmsTouched.Add(realmName);
            LastResetPassword = password;
            LastResetTemporary = temporary;
            return Task.CompletedTask;
        }

        public Task EnsureRealmRoleAsync(string realmName, string roleName, string description, CancellationToken cancellationToken = default)
        {
            RealmsTouched.Add(realmName);
            EnsuredRoles.Add(roleName);
            return Task.CompletedTask;
        }

        public Task AssignRealmRolesAsync(string realmName, string userId, IReadOnlyList<string> roleNames, CancellationToken cancellationToken = default)
        {
            RealmsTouched.Add(realmName);
            AssignedRoles.AddRange(roleNames);
            return Task.CompletedTask;
        }

        public Task DeleteUserAsync(string realmName, string userId, CancellationToken cancellationToken = default)
        {
            RealmsTouched.Add(realmName);
            DeletedUserIds.Add(userId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTenantQueries : ITenantQueries
    {
        private readonly Func<TenantDto?> _tenant;

        public FakeTenantQueries(Func<TenantDto?> tenant) => _tenant = tenant;

        public Task<IReadOnlyList<TenantDto>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TenantDto>>(_tenant() is { } t ? [t] : []);

        public Task<TenantDto?> GetByIdAsync(string tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_tenant());
    }

    /// <summary>Scope tenant factice : fournit ISender + ITenantSettingsQueries + IIdentityQueries du tenant cible.</summary>
    private sealed class FakeTenantScopeFactory : ITenantScopeFactory
    {
        private readonly ISender _sender;
        private readonly Func<Guid?> _profileCompanyId;
        private readonly Func<UserDto?> _existingUser;

        public FakeTenantScopeFactory(ISender sender, Func<Guid?> profileCompanyId, Func<UserDto?> existingUser)
        {
            _sender = sender;
            _profileCompanyId = profileCompanyId;
            _existingUser = existingUser;
        }

        public ITenantScope Create(string tenantId)
        {
            var services = new ServiceCollection();
            services.AddSingleton(_sender);
            services.AddSingleton<ITenantSettingsQueries>(new FakeTenantSettingsQueries(_profileCompanyId));
            services.AddSingleton<IIdentityQueries>(new FakeIdentityQueries(_existingUser));
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
        private readonly Func<Guid?> _companyId;

        public FakeTenantSettingsQueries(Func<Guid?> companyId) => _companyId = companyId;

        public Task<Guid?> GetCurrentCompanyId(CancellationToken ct = default) => Task.FromResult(_companyId());

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

    private sealed class RecordingSender : ISender
    {
        public List<object> Sent { get; } = [];

        public Guid CreatedUserId { get; } = Guid.NewGuid();

        public Exception? ThrowOnSend { get; set; }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            if (ThrowOnSend is not null)
            {
                throw ThrowOnSend;
            }

            Sent.Add(request!);
            return Task.CompletedTask;
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (ThrowOnSend is not null)
            {
                throw ThrowOnSend;
            }

            Sent.Add(request);
            object response = request switch
            {
                CreateUserCommand => CreatedUserId,
                _ => throw new NotSupportedException(request.GetType().Name),
            };

            return Task.FromResult((TResponse)response);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
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

    private sealed class FakeIdentityQueries : IIdentityQueries
    {
        private readonly Func<UserDto?> _existingUser;

        public FakeIdentityQueries(Func<UserDto?> existingUser) => _existingUser = existingUser;

        public Task<UserDto?> GetUserByUsername(string username, CancellationToken ct = default) =>
            Task.FromResult(_existingUser());

        public Task<IReadOnlyList<UserDto>> ListUsers(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<UserDto?> GetUserById(Guid userId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<UserDto?> GetUserByEmail(string email, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> GetUserPermissions(Guid userId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> UserHasPermission(Guid userId, string permission, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<GrantConditionDto>> GetUserGrantsForPermission(Guid userId, string permission, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<RoleDto>> GetRoles(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<RoleDetailDto?> GetRoleById(Guid roleId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<RoleUserDto>> GetUsersForRole(Guid roleId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
