namespace Liakont.Host.Startup;

using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stratum.Modules.Audit.Contracts;
using Stratum.Modules.Identity.Contracts;
using Stratum.Modules.Identity.Contracts.Commands;
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

        if (string.IsNullOrWhiteSpace(options.ExternalId))
        {
            LogAdminSeedSkipped(logger);
            return;
        }

        var existing = await identityQueries.GetUserByUsername(options.Username);

        if (existing is null)
        {
            LogSeedingAdmin(logger, options.Username);

            var userId = await sender.Send(new CreateUserCommand
            {
                Username = options.Username,
                Email = options.Email,
                DisplayName = options.DisplayName,
                ExternalId = options.ExternalId,
            });

            await sender.Send(new AssignUserRoleCommand
            {
                UserId = userId,
                RoleName = "Admin",
            });

            LogAdminSeeded(logger, options.Username);
        }
        else
        {
            // Back-fill ExternalId for pre-OIDC admin users so UserSyncService
            // can match them by ExternalId on first Keycloak login.
            if (string.IsNullOrWhiteSpace(existing.ExternalId)
                && !string.IsNullOrWhiteSpace(options.ExternalId))
            {
                LogLinkingExternalId(logger, options.Username, options.ExternalId);

                await sender.Send(new LinkExternalIdCommand
                {
                    UserId = existing.Id,
                    ExternalId = options.ExternalId,
                });
            }

            LogAdminAlreadyExists(logger, options.Username);
        }

        // Grant permissions on the Admin role unconditionally — GrantPermissionCommand
        // is idempotent (upsert). This ensures new permissions added to AdminPermissions
        // are applied on re-deployment even when the user already exists.
        foreach (var permission in AdminPermissions)
        {
            await sender.Send(new GrantPermissionCommand
            {
                RoleName = "Admin",
                Permission = permission,
                ModuleSource = "seed",
            });
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
}
