namespace Liakont.Host.Startup;

using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stratum.Modules.Audit.Contracts;
using Stratum.Modules.Identity.Contracts;
using Stratum.Modules.Identity.Contracts.Commands;
using Stratum.Modules.Identity.Contracts.DTOs;
using Stratum.Modules.Identity.Contracts.Queries;
using Stratum.Modules.Job.Contracts;
using Stratum.Modules.Notification.Contracts;

internal static partial class AdminUserSeeder
{
    private static readonly string[] AdminPermissions =
    [
        IdentityPermissions.UserView,
        IdentityPermissions.UserCreate,
        IdentityPermissions.UserUpdate,
        IdentityPermissions.UserDeactivate,
        IdentityPermissions.RolesManage,
        NotificationPermissions.SlaView,
        NotificationPermissions.SlaCreate,
        NotificationPermissions.SlaUpdate,
        NotificationPermissions.SlaDelete,
        NotificationPermissions.View,
        NotificationPermissions.Create,
        NotificationPermissions.Update,
        NotificationPermissions.Send,
        NotificationPermissions.RoutingRuleView,
        NotificationPermissions.RoutingRuleCreate,
        NotificationPermissions.RoutingRuleUpdate,
        NotificationPermissions.RoutingRuleDelete,
        NotificationPermissions.ServiceView,
        NotificationPermissions.ServiceCreate,
        NotificationPermissions.DeliveryView,
        AuditPermissions.AuditView,
        AuditPermissions.AuditExport,
        JobPermissions.View,
        JobPermissions.ManageSchedules,
    ];

    public static async Task SeedAdminUserAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();

        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AdminSeedOptions>>();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var identityQueries = scope.ServiceProvider.GetRequiredService<IIdentityQueries>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<AdminSeedOptions>>().Value;

        await SeedAsync(sender, identityQueries, options, logger);
    }

    /// <summary>
    /// Cœur d'amorçage TESTABLE, isolé du conteneur DI. ROBUSTE par conception (FIX203b) : il ne doit
    /// JAMAIS faire planter le Host. Cas réel (bug-inbox recette run 2) : <c>AdminSeed:ExternalId</c>
    /// pointe sur le sub d'un compte déjà auto-provisionné à la connexion OIDC (sous un AUTRE username) ;
    /// le <see cref="CreateUserCommand"/> violait alors la contrainte unique <c>ix_users_external_id</c>
    /// (PostgreSQL 23505) NON rattrapée → exit du Host. Ici : on tente la création, et tout échec est
    /// journalisé sans propager (l'accès super-admin sous OIDC vient du rôle realm <c>stratum-admin</c>,
    /// pas de cet utilisateur Identity — le démarrage se poursuit).
    /// </summary>
    internal static async Task SeedAsync(
        ISender sender,
        IIdentityQueries identityQueries,
        AdminSeedOptions options,
        ILogger logger,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.ExternalId))
        {
            LogAdminSeedSkipped(logger);
            return;
        }

        try
        {
            var existing = await identityQueries.GetUserByUsername(options.Username, ct);

            if (existing is null)
            {
                await TrySeedNewAdminAsync(sender, options, logger, ct);
            }
            else
            {
                await PromoteExistingAdminAsync(sender, existing, options, logger, ct);
            }

            // Grant permissions on the Admin role unconditionally — GrantPermissionCommand
            // is idempotent (upsert). This ensures new permissions added to AdminPermissions
            // are applied on re-deployment even when the user already exists.
            foreach (var permission in AdminPermissions)
            {
                await sender.Send(
                    new GrantPermissionCommand
                    {
                        RoleName = "Admin",
                        Permission = permission,
                        ModuleSource = "seed",
                    },
                    ct);
            }
        }
        catch (Exception ex)
        {
            // Dernier rempart (FIX203b) : l'amorçage de l'admin ne doit JAMAIS faire planter le Host.
            LogAdminSeedFailed(logger, ex);
        }
    }

    /// <summary>
    /// Crée l'utilisateur admin. Si un utilisateur existe DÉJÀ avec cet ExternalId (auto-provisionné à
    /// la connexion OIDC sous un autre username), l'INSERT viole <c>ix_users_external_id</c> : on
    /// journalise et on continue (no-op explicite), sans planter le Host.
    /// </summary>
    private static async Task TrySeedNewAdminAsync(
        ISender sender,
        AdminSeedOptions options,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            LogSeedingAdmin(logger, options.Username);

            var userId = await sender.Send(
                new CreateUserCommand
                {
                    Username = options.Username,
                    Email = options.Email,
                    DisplayName = options.DisplayName,
                    ExternalId = options.ExternalId,
                },
                ct);

            await EnsureAdminRoleAsync(sender, userId, logger, ct);

            LogAdminSeeded(logger, options.Username);
        }
        catch (Exception ex)
        {
            // Cas 23505 (ou tout autre échec de création) : un utilisateur porte déjà cet ExternalId.
            // L'accès super-admin sous OIDC vient du rôle realm stratum-admin — le Host doit démarrer.
            LogAdminSeedExternalIdConflict(logger, options.Username, options.ExternalId, ex);
        }
    }

    /// <summary>
    /// Utilisateur admin déjà présent (par username) : on rattache l'ExternalId si manquant (admins
    /// pré-OIDC) et on garantit le rôle Admin (promotion idempotente), puis on continue.
    /// </summary>
    private static async Task PromoteExistingAdminAsync(
        ISender sender,
        UserDto existing,
        AdminSeedOptions options,
        ILogger logger,
        CancellationToken ct)
    {
        // Back-fill ExternalId for pre-OIDC admin users so UserSyncService
        // can match them by ExternalId on first Keycloak login.
        if (string.IsNullOrWhiteSpace(existing.ExternalId)
            && !string.IsNullOrWhiteSpace(options.ExternalId))
        {
            LogLinkingExternalId(logger, options.Username, options.ExternalId);

            await sender.Send(
                new LinkExternalIdCommand
                {
                    UserId = existing.Id,
                    ExternalId = options.ExternalId,
                },
                ct);
        }

        await EnsureAdminRoleAsync(sender, existing.Id, logger, ct);

        LogAdminAlreadyExists(logger, options.Username);
    }

    /// <summary>
    /// Garantit le rôle Admin de l'utilisateur de façon IDEMPOTENTE : l'assignation de rôle lève
    /// INV-IDENTITY-003 si le rôle est déjà présent — ce cas est avalé (rien à faire).
    /// </summary>
    private static async Task EnsureAdminRoleAsync(ISender sender, Guid userId, ILogger logger, CancellationToken ct)
    {
        try
        {
            await sender.Send(new AssignUserRoleCommand { UserId = userId, RoleName = "Admin" }, ct);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("INV-IDENTITY-003", StringComparison.Ordinal))
        {
            LogAdminRoleAlreadyPresent(logger, userId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "AdminSeed:ExternalId is not configured — skipping admin seed. Set AdminSeed:ExternalId to the Keycloak subject ID.")]
    private static partial void LogAdminSeedSkipped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin user '{Username}' already exists — skipping seed.")]
    private static partial void LogAdminAlreadyExists(ILogger logger, string username);

    [LoggerMessage(Level = LogLevel.Information, Message = "Linking ExternalId '{ExternalId}' to existing admin user '{Username}'.")]
    private static partial void LogLinkingExternalId(ILogger logger, string username, string externalId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeding initial admin user '{Username}'.")]
    private static partial void LogSeedingAdmin(ILogger logger, string username);

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin user '{Username}' seeded successfully.")]
    private static partial void LogAdminSeeded(ILogger logger, string username);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "AdminSeed : un utilisateur porte déjà l'ExternalId '{ExternalId}' (déjà auto-provisionné "
            + "à la connexion OIDC sous un autre nom que '{Username}') — création ignorée, le Host continue. "
            + "L'accès super-admin vient du rôle realm 'stratum-admin'.")]
    private static partial void LogAdminSeedExternalIdConflict(ILogger logger, string username, string externalId, Exception reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "AdminSeed : l'utilisateur '{UserId}' possède déjà le rôle Admin — rien à faire.")]
    private static partial void LogAdminRoleAlreadyPresent(ILogger logger, Guid userId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "AdminSeed : l'amorçage de l'administrateur a échoué — le Host démarre malgré tout "
            + "(l'accès super-admin sous OIDC vient du rôle realm 'stratum-admin').")]
    private static partial void LogAdminSeedFailed(ILogger logger, Exception exception);
}
