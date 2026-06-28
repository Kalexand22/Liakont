namespace Liakont.Host.MultiTenancy;

using Liakont.Host.Security.Abstractions;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Maps REST endpoints for tenant administration under <c>/admin/tenants</c>.
/// All endpoints require the <c>SystemAdmin</c> role.
/// </summary>
public static class TenantAdminEndpointMapping
{
    public static IEndpointRouteBuilder MapTenantAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var tenants = app.MapGroup("/admin/tenants")
            .RequireAuthorization(p => p.RequireRole("SystemAdmin"));

        tenants.MapGet("/", HandleListAsync);
        tenants.MapPost("/", HandleCreateAsync);
        tenants.MapGet("/{tenantId}", HandleGetByIdAsync);
        tenants.MapDelete("/{tenantId}", HandleDeleteAsync);
        tenants.MapPost("/{tenantId}/reprovision", HandleReprovisionAsync);
        tenants.MapPost("/{tenantId}/seed", HandleSeedAsync);
        tenants.MapPost("/{tenantId}/users", HandleCreateUserAsync);

        return app;
    }

    internal static async Task<IResult> HandleListAsync(
        ITenantQueries queries, CancellationToken ct)
    {
        var result = await queries.ListAsync(ct);
        return Results.Ok(result);
    }

    internal static async Task<IResult> HandleCreateAsync(
        CreateTenantApiRequest body,
        ITenantProvisioningService provisioner,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.TenantId)
            || string.IsNullOrWhiteSpace(body.DisplayName)
            || string.IsNullOrWhiteSpace(body.AdminEmail))
        {
            return Results.BadRequest(new { ErrorMessage = "TenantId, DisplayName, and AdminEmail are required." });
        }

        var request = new TenantProvisionRequest
        {
            TenantId = body.TenantId,
            DisplayName = body.DisplayName,
            AdminEmail = body.AdminEmail,
        };

        var result = await provisioner.ProvisionAsync(request, ct);

        if (!result.Success)
        {
            return Results.BadRequest(new { result.ErrorMessage });
        }

        if (result.AlreadyProvisioned)
        {
            return Results.Ok(new { result.DatabaseName, result.RealmName, result.Authority, AlreadyProvisioned = true });
        }

        return Results.Created(
            $"/api/v1/admin/tenants/{body.TenantId}",
            new { result.DatabaseName, result.RealmName, result.Authority });
    }

    internal static async Task<IResult> HandleGetByIdAsync(
        string tenantId, ITenantQueries queries, CancellationToken ct)
    {
        var result = await queries.GetByIdAsync(tenantId, ct);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    internal static async Task<IResult> HandleDeleteAsync(
        string tenantId, ITenantProvisioningService provisioner, CancellationToken ct)
    {
        var result = await provisioner.DeactivateAsync(tenantId, ct);

        if (result.Success)
        {
            return Results.NoContent();
        }

        if (result.TenantNotFound)
        {
            return Results.NotFound();
        }

        return Results.BadRequest(new { result.ErrorMessage });
    }

    internal static async Task<IResult> HandleReprovisionAsync(
        string tenantId,
        ITenantProvisioningService provisioner,
        CancellationToken ct)
    {
        var result = await provisioner.ReprovisionAsync(tenantId, ct);

        if (result.Success)
        {
            return Results.Ok(new { result.DatabaseName, result.MigrationsApplied, Reprovisioned = true });
        }

        if (result.TenantNotFound)
        {
            return Results.NotFound();
        }

        if (result.TenantDeactivated)
        {
            return Results.UnprocessableEntity(new { result.ErrorMessage });
        }

        return Results.BadRequest(new { result.ErrorMessage });
    }

    /// <summary>
    /// Importe (idempotent) le seed de paramétrage d'un tenant (profil légal + fiscal + planification +
    /// seuils + comptes PA sans secret) depuis un dossier serveur au format <c>config/exemples/tenant-seed/</c>
    /// (chemin de provisioning OPS03). Établit le scope du tenant cible (<see cref="ITenantScopeFactory"/>)
    /// puis dispatche <see cref="ImportTenantSeedCommand"/> avec son <c>companyId</c> (clé de scoping —
    /// celle du claim <c>company_id</c> du realm). N'écrit JAMAIS de secret (INV-TENANTSETTINGS-007 : les
    /// clés API restent vides, à saisir via la console). SystemAdmin uniquement.
    /// </summary>
    internal static async Task<IResult> HandleSeedAsync(
        string tenantId,
        SeedTenantApiRequest body,
        ITenantQueries queries,
        ITenantScopeFactory scopeFactory,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.SeedDirectoryPath))
        {
            return Results.BadRequest(new { ErrorMessage = "SeedDirectoryPath is required." });
        }

        // Le tenant doit exister (sa base est déjà provisionnée) avant qu'on y importe un paramétrage.
        var tenant = await queries.GetByIdAsync(tenantId, ct);
        if (tenant is null)
        {
            return Results.NotFound();
        }

        // companyId : la valeur du REGISTRE (fixée au provisioning, émise par le claim du realm) est
        // la référence. Un CompanyId explicite reste accepté (tenants antérieurs au registre porteur)
        // mais une DIVERGENCE avec le registre est refusée — un seed scopé sur une autre société que
        // celle du realm rendrait toutes les données du tenant invisibles à ses utilisateurs.
        var companyId = body.CompanyId == Guid.Empty ? tenant.CompanyId : body.CompanyId;
        if (companyId is null || companyId == Guid.Empty)
        {
            return Results.BadRequest(new
            {
                ErrorMessage = "CompanyId is required (tenant provisionné sans company_id au registre).",
            });
        }

        if (tenant.CompanyId is { } registered && registered != companyId)
        {
            return Results.Conflict(new
            {
                ErrorMessage = "Le CompanyId fourni ne correspond pas à celui du realm provisionné — "
                    + "le seed serait invisible aux utilisateurs du tenant. Omettez CompanyId pour utiliser celui du registre.",
            });
        }

        // Scope du tenant CIBLE (la requête SystemAdmin n'est pas scopée sur lui) : la connexion est routée
        // vers sa base et le companyId explicite est la clé de scoping écrite (aucun profil n'existe encore).
        await using var scope = scopeFactory.Create(tenantId);

        // PROVISIONING create-only : si le tenant a DÉJÀ du paramétrage, refuser (409) plutôt que de réimporter
        // — un re-seed remettrait des réglages saisis via la console à la baseline du seed. La reconfiguration
        // passe par la console, jamais par un ré-import de provisioning. Ancré sur la présence d'UN composant de
        // paramétrage (fiscal/planif/seuils/compte PA), et non plus sur le profil : l'identité légale n'étant
        // plus seedée (BUG-14), le profil ne marque plus « tenant paramétré » ; et un seed dont le seul bloc est
        // p.ex. le planning n'écrirait aucun fiscal — ancrer sur le seul fiscal laisserait un tel ré-import
        // écraser silencieusement des réglages édités via la console.
        var settingsQueries = scope.Services.GetRequiredService<ITenantSettingsQueries>();
        if (await settingsQueries.HasAnyConfigurationAsync(companyId.Value, ct))
        {
            return Results.Conflict(new { ErrorMessage = "Tenant déjà paramétré — reconfigurez via la console, pas par un ré-import de provisioning." });
        }

        var sender = scope.Services.GetRequiredService<ISender>();
        var result = await sender.Send(
            new ImportTenantSeedCommand { SeedDirectoryPath = body.SeedDirectoryPath, CompanyId = companyId },
            ct);

        return Results.Ok(result);
    }

    /// <summary>
    /// Provisionne un utilisateur de TENANT (compte IdP dans le realm du tenant + rôle realm standard +
    /// compte applicatif + invitation email) via <see cref="ITenantUserProvisioningService"/> — le point
    /// d'entrée HTTP du flux « premier utilisateur » de l'assistant OPS03. Le mot de passe temporaire
    /// n'apparaît dans la réponse QUE si aucune invitation email n'a pu partir (SMTP non configuré) ;
    /// il n'est jamais journalisé ni persisté. SystemAdmin uniquement (groupe).
    /// </summary>
    internal static async Task<IResult> HandleCreateUserAsync(
        string tenantId,
        CreateTenantUserApiRequest body,
        ITenantUserProvisioningService userProvisioning,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Email)
            || string.IsNullOrWhiteSpace(body.Username)
            || string.IsNullOrWhiteSpace(body.DisplayName)
            || string.IsNullOrWhiteSpace(body.Role))
        {
            return Results.BadRequest(new { ErrorMessage = "Email, Username, DisplayName et Role sont obligatoires." });
        }

        var result = await userProvisioning.ProvisionUserAsync(
            new TenantUserProvisionRequest
            {
                TenantId = tenantId,
                Email = body.Email,
                Username = body.Username,
                DisplayName = body.DisplayName,
                Role = body.Role,
            },
            ct);

        if (!result.Success)
        {
            // Le code HTTP se mappe sur la cause TYPÉE — jamais sur le message français (qui reste
            // purement opérateur et peut être reformulé sans changer le contrat HTTP).
            return result.FailureReason switch
            {
                TenantUserProvisionFailureReason.TenantNotFound => Results.NotFound(new { result.ErrorMessage }),
                TenantUserProvisionFailureReason.Conflict => Results.Conflict(new { result.ErrorMessage }),
                _ => Results.BadRequest(new { result.ErrorMessage }),
            };
        }

        return Results.Created(
            $"/admin/tenants/{tenantId}/users/{result.UserId}",
            new { result.UserId, result.IdpUserId, result.InvitationEmailSent, result.TemporaryPassword });
    }

    /// <summary>API request body for creating a new tenant.</summary>
    public sealed record CreateTenantApiRequest(string TenantId, string DisplayName, string AdminEmail);

    /// <summary>
    /// Corps de requête de l'import de seed d'un tenant. <paramref name="CompanyId"/> = société (companyId)
    /// du tenant cible (claim <c>company_id</c> du realm) — optionnel (Guid.Empty) : repli sur la valeur du
    /// registre fixée au provisioning ; <paramref name="SeedDirectoryPath"/> = dossier serveur du seed
    /// (format <c>config/exemples/tenant-seed/</c>). Aucune clé API n'est jamais importée.
    /// </summary>
    public sealed record SeedTenantApiRequest(Guid CompanyId, string SeedDirectoryPath);

    /// <summary>
    /// Corps de requête du provisioning d'un utilisateur de tenant. <paramref name="Role"/> = rôle realm
    /// standard (lecture | operateur | parametrage | superviseur — matrice §3, aucun rôle inventé).
    /// </summary>
    public sealed record CreateTenantUserApiRequest(string Email, string Username, string DisplayName, string Role);
}
