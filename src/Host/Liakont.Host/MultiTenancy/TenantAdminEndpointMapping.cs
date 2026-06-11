namespace Liakont.Host.MultiTenancy;

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

        return Results.Created($"/api/v1/admin/tenants/{body.TenantId}", new { result.DatabaseName, result.RealmName, result.Authority });
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
        if (body.CompanyId == Guid.Empty || string.IsNullOrWhiteSpace(body.SeedDirectoryPath))
        {
            return Results.BadRequest(new { ErrorMessage = "CompanyId and SeedDirectoryPath are required." });
        }

        // Le tenant doit exister (sa base est déjà provisionnée) avant qu'on y importe un paramétrage.
        var tenant = await queries.GetByIdAsync(tenantId, ct);
        if (tenant is null)
        {
            return Results.NotFound();
        }

        // Scope du tenant CIBLE (la requête SystemAdmin n'est pas scopée sur lui) : la connexion est routée
        // vers sa base et le companyId explicite est la clé de scoping écrite (aucun profil n'existe encore).
        await using var scope = scopeFactory.Create(tenantId);

        // PROVISIONING create-only : si le tenant a DÉJÀ un profil, refuser (409) plutôt que de réimporter
        // — un re-seed remettrait des réglages fiscaux saisis via la console à la baseline du seed (null).
        // La reconfiguration passe par la console, jamais par un ré-import de provisioning.
        var settingsQueries = scope.Services.GetRequiredService<ITenantSettingsQueries>();
        if (await settingsQueries.GetCurrentCompanyId(ct) is not null)
        {
            return Results.Conflict(new { ErrorMessage = "Tenant déjà paramétré (profil existant) — reconfigurez via la console, pas par un ré-import de provisioning." });
        }

        var sender = scope.Services.GetRequiredService<ISender>();
        var result = await sender.Send(
            new ImportTenantSeedCommand { SeedDirectoryPath = body.SeedDirectoryPath, CompanyId = body.CompanyId },
            ct);

        return Results.Ok(result);
    }

    /// <summary>API request body for creating a new tenant.</summary>
    public sealed record CreateTenantApiRequest(string TenantId, string DisplayName, string AdminEmail);

    /// <summary>
    /// Corps de requête de l'import de seed d'un tenant. <paramref name="CompanyId"/> = société (companyId)
    /// du tenant cible (claim <c>company_id</c> du realm) ; <paramref name="SeedDirectoryPath"/> = dossier
    /// serveur du seed (format <c>config/exemples/tenant-seed/</c>). Aucune clé API n'est jamais importée.
    /// </summary>
    public sealed record SeedTenantApiRequest(Guid CompanyId, string SeedDirectoryPath);
}
