namespace Liakont.Host.MultiTenancy;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
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

    /// <summary>API request body for creating a new tenant.</summary>
    public sealed record CreateTenantApiRequest(string TenantId, string DisplayName, string AdminEmail);
}
