namespace Liakont.Host.Security.Keycloak;

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Host.Notifications;
using Liakont.Host.Security.Abstractions;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.Keycloak;
using Stratum.Modules.Identity.Contracts.Commands;
using Stratum.Modules.Identity.Contracts.Queries;
using Stratum.Modules.Notification.Contracts;

/// <summary>
/// Implémentation Keycloak de <see cref="ITenantUserProvisioningService"/> (couche d'auth du Host —
/// seul endroit autorisé à parler à l'IdP concret, blueprint §6). Séquence : compte Keycloak dans le
/// realm du tenant (mot de passe temporaire aléatoire, changement forcé à la première connexion) →
/// rôle realm standard (créé idempotent : les realms provisionnés par le socle n'ont que les rôles
/// Stratum) → compte applicatif <c>identity.users</c> dans la base TENANT (un compte applicatif
/// PRÉEXISTANT du même username est RELIÉ — reprise idempotente après un échec antérieur dont la
/// compensation n'a supprimé que le compte IdP ; sinon compensation : le compte IdP est supprimé) →
/// attributs (<c>stratum_user_id</c>, et <c>company_id</c> pour les realms antérieurs au mapper
/// hardcodé) → invitation email envoyée de façon SYNCHRONE via <see cref="IEmailTransport"/> — le
/// mot de passe temporaire n'est JAMAIS persisté (ni file de jobs, ni logs : règle n°18) ; sans
/// SMTP configuré (ou envoi en échec), il est remis UNE FOIS à l'appelant.
/// </summary>
internal sealed partial class KeycloakTenantUserProvisioner : ITenantUserProvisioningService
{
    private readonly ITenantQueries _tenantQueries;
    private readonly ITenantScopeFactory _scopeFactory;
    private readonly IKeycloakUserProvisioner _keycloak;
    private readonly IEmailTransport _emailTransport;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly SmtpOptions _smtpOptions;
    private readonly KeycloakAdminOptions _keycloakOptions;
    private readonly ILogger<KeycloakTenantUserProvisioner> _logger;

    public KeycloakTenantUserProvisioner(
        ITenantQueries tenantQueries,
        ITenantScopeFactory scopeFactory,
        IKeycloakUserProvisioner keycloak,
        IEmailTransport emailTransport,
        IHttpContextAccessor httpContextAccessor,
        IOptions<SmtpOptions> smtpOptions,
        IOptions<KeycloakAdminOptions> keycloakOptions,
        ILogger<KeycloakTenantUserProvisioner> logger)
    {
        _tenantQueries = tenantQueries;
        _scopeFactory = scopeFactory;
        _keycloak = keycloak;
        _emailTransport = emailTransport;
        _httpContextAccessor = httpContextAccessor;
        _smtpOptions = smtpOptions.Value;
        _keycloakOptions = keycloakOptions.Value;
        _logger = logger;
    }

    public async Task<TenantUserProvisionResult> ProvisionUserAsync(
        TenantUserProvisionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!LiakontRealmRoles.IsKnown(request.Role))
        {
            return TenantUserProvisionResult.Failed(
                $"Rôle inconnu : « {request.Role} ». Les rôles valides sont : {string.Join(", ", LiakontRealmRoles.All)}.");
        }

        if (string.IsNullOrWhiteSpace(request.Email)
            || string.IsNullOrWhiteSpace(request.Username)
            || string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return TenantUserProvisionResult.Failed(
                "L'email, le nom d'utilisateur et le nom affiché sont obligatoires.");
        }

        if (!_keycloakOptions.IsConfigured)
        {
            return TenantUserProvisionResult.Failed(
                "Le fournisseur d'identité n'est pas configuré sur cette instance : impossible de créer le compte. "
                + "Renseignez la section Keycloak des paramètres serveur.");
        }

        var tenant = await _tenantQueries.GetByIdAsync(request.TenantId, cancellationToken);
        if (tenant is null)
        {
            return TenantUserProvisionResult.Failed(
                $"Tenant « {request.TenantId} » introuvable — créez d'abord le client.",
                TenantUserProvisionFailureReason.TenantNotFound);
        }

        if (!tenant.IsActive)
        {
            return TenantUserProvisionResult.Failed(
                $"Tenant « {request.TenantId} » désactivé — aucun utilisateur ne peut y être créé.");
        }

        if (string.IsNullOrWhiteSpace(tenant.RealmName))
        {
            return TenantUserProvisionResult.Failed(
                $"Tenant « {request.TenantId} » sans realm d'identité — re-provisionnez le client avec Keycloak configuré.");
        }

        var companyId = await ResolveCompanyIdAsync(request.TenantId, tenant, cancellationToken);
        if (companyId is null)
        {
            return TenantUserProvisionResult.Failed(
                $"Aucun identifiant de société (company_id) connu pour le tenant « {request.TenantId} » — "
                + "importez d'abord son seed de paramétrage (POST /admin/tenants/{id}/seed) ou re-provisionnez-le.");
        }

        if (await _keycloak.FindUserIdByUsernameAsync(tenant.RealmName, request.Username, cancellationToken) is not null)
        {
            return TenantUserProvisionResult.Failed(
                $"L'utilisateur « {request.Username} » existe déjà dans le realm du tenant « {request.TenantId} ».",
                TenantUserProvisionFailureReason.Conflict);
        }

        // Le mot de passe temporaire ne quitte cette méthode QUE par l'invitation email (envoi
        // synchrone, jamais persisté) ou par le résultat (une seule fois) — jamais par les logs.
        var temporaryPassword = GenerateTemporaryPassword();

        string kcUserId;
        try
        {
            kcUserId = await _keycloak.CreateUserAsync(
                tenant.RealmName,
                new KeycloakUserSpec
                {
                    Username = request.Username,
                    Email = request.Email,
                    LastName = request.DisplayName,
                    EmailVerified = true,
                    RequiredActions = ["UPDATE_PASSWORD"],
                },
                cancellationToken);
        }
        catch (KeycloakUserConflictException)
        {
            // L'email est unique par realm : un conflit peut survenir malgré le pré-contrôle du
            // username — refus opérateur propre, jamais un 500.
            return TenantUserProvisionResult.Failed(
                $"Un compte avec ce nom d'utilisateur ou cet email existe déjà dans le realm du tenant « {request.TenantId} ».",
                TenantUserProvisionFailureReason.Conflict);
        }

        try
        {
            await _keycloak.ResetPasswordAsync(tenant.RealmName, kcUserId, temporaryPassword, temporary: true, cancellationToken);

            // Les realms provisionnés par le socle ne portent que les rôles Stratum : le rôle
            // standard Liakont est créé à la volée, idempotent (couvre aussi les realms anciens).
            await _keycloak.EnsureRealmRoleAsync(
                tenant.RealmName, request.Role, LiakontRealmRoles.Descriptions[request.Role], cancellationToken);
            await _keycloak.AssignRealmRolesAsync(tenant.RealmName, kcUserId, [request.Role], cancellationToken);

            // Compte applicatif dans la base du TENANT (scope cible) : ExternalId = sub Keycloak,
            // pour que le sync au premier login retrouve CE compte (jamais de doublon). Un compte
            // applicatif PRÉEXISTANT du même username (reliquat d'un échec antérieur — la
            // compensation ne supprime que le compte IdP — ou compte pré-OIDC) est RELIÉ via
            // LinkExternalIdCommand (son usage documenté) : la reprise est idempotente, jamais un
            // utilisateur « définitivement non provisionnable ».
            //
            // EXÉCUTION EN CONTEXTE SYSTÈME (HttpContext ambiant suspendu le temps du Send) : la garde
            // du CreateUserHandler socle vise l'écran d'administration Identity (un utilisateur
            // authentifié doit porter identity.users.create) et considère les appels NON authentifiés
            // comme du provisioning système légitime (même chemin qu'AdminUserSeeder au démarrage).
            // Notre appel EST du provisioning d'instance, déjà autorisé en amont (endpoint /admin
            // gardé SystemAdmin ; page console gardée liakont.supervision) — l'opérateur d'instance
            // ne porte jamais les permissions identity.* du tenant cible (matrice §3). Le contexte
            // est RESTAURÉ en finally : le reste de la requête garde son acteur.
            Guid userId;
            var ambientHttpContext = _httpContextAccessor.HttpContext;
            _httpContextAccessor.HttpContext = null;
            try
            {
                await using var scope = _scopeFactory.Create(request.TenantId);
                var sender = scope.Services.GetRequiredService<ISender>();
                var identityQueries = scope.Services.GetRequiredService<IIdentityQueries>();

                var existing = await identityQueries.GetUserByUsername(request.Username, cancellationToken);
                if (existing is not null)
                {
                    userId = existing.Id;
                    await sender.Send(
                        new LinkExternalIdCommand { UserId = userId, ExternalId = kcUserId },
                        cancellationToken);
                }
                else
                {
                    userId = await sender.Send(
                        new CreateUserCommand
                        {
                            Username = request.Username,
                            Email = request.Email,
                            DisplayName = request.DisplayName,
                            ExternalId = kcUserId,
                        },
                        cancellationToken);
                }
            }
            finally
            {
                _httpContextAccessor.HttpContext = ambientHttpContext;
            }

            // stratum_user_id DOIT égaler identity.users.id (le mapper d'attribut du realm fait foi
            // sur le chemin JwtBearer). company_id : porté par le mapper hardcodé des realms récents ;
            // posé AUSSI en attribut pour les realms antérieurs (mapper attribut) — sans divergence
            // possible, la valeur vient de la même source.
            await _keycloak.SetUserAttributesAsync(
                tenant.RealmName,
                kcUserId,
                new Dictionary<string, string>
                {
                    ["stratum_user_id"] = userId.ToString(),
                    ["company_id"] = companyId.Value.ToString(),
                },
                cancellationToken);

            var invitationSent = await TrySendInvitationAsync(request, temporaryPassword, cancellationToken);

            LogUserProvisioned(_logger, request.Username, request.TenantId, request.Role, invitationSent);

            return new TenantUserProvisionResult
            {
                Success = true,
                UserId = userId,
                IdpUserId = kcUserId,
                InvitationEmailSent = invitationSent,

                // Remis une seule fois à l'opérateur SEULEMENT si aucune invitation n'est partie :
                // si l'email part, le mot de passe n'est restitué nulle part ailleurs.
                TemporaryPassword = invitationSent ? null : temporaryPassword,
            };
        }
        catch
        {
            // Compensation : ne jamais laisser un compte IdP orphelin (sans compte applicatif ni
            // attributs de scope) — l'appelant pourra rejouer la création de zéro.
            await _keycloak.DeleteUserAsync(tenant.RealmName, kcUserId, CancellationToken.None);
            throw;
        }
    }

    /// <summary>Mot de passe temporaire aléatoire (24 caractères base64url) — l'IdP force le changement.</summary>
    private static string GenerateTemporaryPassword() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(18))
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Utilisateur '{Username}' provisionné pour le tenant '{TenantId}' (rôle '{Role}', invitation email : {InvitationSent}).")]
    private static partial void LogUserProvisioned(
        ILogger logger, string username, string tenantId, string role, bool invitationSent);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Envoi de l'invitation impossible pour '{Username}' (tenant '{TenantId}') — le mot de passe temporaire est remis à l'opérateur.")]
    private static partial void LogInvitationSendFailed(ILogger logger, string username, string tenantId, Exception exception);

    /// <summary>
    /// Société du tenant : le profil (base tenant) fait foi dès qu'il existe ; sinon le registre de
    /// provisioning (<c>outbox.tenants.company_id</c>) ; sinon inconnue (le seed n'a pas été importé
    /// et le tenant prédate le registre porteur).
    /// </summary>
    private async Task<Guid?> ResolveCompanyIdAsync(string tenantId, TenantDto tenant, CancellationToken ct)
    {
        await using var scope = _scopeFactory.Create(tenantId);
        var settingsQueries = scope.Services.GetRequiredService<ITenantSettingsQueries>();
        var fromProfile = await settingsQueries.GetCurrentCompanyId(ct);
        return fromProfile ?? tenant.CompanyId;
    }

    /// <summary>
    /// Envoie l'invitation de façon SYNCHRONE (jamais par la file de jobs : le mot de passe
    /// temporaire ne doit JAMAIS être persisté — règle n°18). Retourne <c>false</c> si le SMTP
    /// n'est pas configuré OU si l'envoi échoue (tracé) — l'appelant remet alors le mot de passe
    /// temporaire à l'opérateur, une seule fois.
    /// </summary>
    private async Task<bool> TrySendInvitationAsync(
        TenantUserProvisionRequest request, string temporaryPassword, CancellationToken ct)
    {
        if (!_smtpOptions.IsConfigured)
        {
            return false;
        }

        var consoleUrl = _keycloakOptions.AppBaseUrl.TrimEnd('/');
        var body =
            $"""
            Bonjour {request.DisplayName},

            Un accès à la console Liakont vient d'être créé pour vous.

            Nom d'utilisateur : {request.Username}
            Mot de passe temporaire : {temporaryPassword}

            À votre première connexion, vous devrez choisir un nouveau mot de passe.
            {(string.IsNullOrWhiteSpace(consoleUrl) ? string.Empty : $"Connexion : {consoleUrl}")}

            Si vous n'êtes pas à l'origine de cette demande, contactez votre opérateur Liakont.
            """;

        try
        {
            await _emailTransport.SendAsync(request.Email, "Votre accès à la console Liakont", body, ct);
            return true;
        }
        catch (Exception ex)
        {
            // L'échec d'envoi n'avorte pas le provisioning : le compte existe, l'opérateur reçoit le
            // mot de passe à l'écran (une fois). Tracé sans le contenu du message (le corps porte le secret).
            LogInvitationSendFailed(_logger, request.Username, request.TenantId, ex);
            return false;
        }
    }
}
