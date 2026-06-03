namespace Stratum.Modules.Identity.Web;

using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Stratum.Modules.Identity.Contracts.Commands;
using Stratum.Modules.Identity.Contracts.Queries;

public static class IdentityEndpointMapping
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        var users = app.MapGroup("/users").RequireAuthorization(p => p.RequireRole("Admin"));

        users.MapPost("/", async (CreateUserCommand command, ISender sender, CancellationToken ct) =>
        {
            var id = await sender.Send(command, ct);
            return Results.Created($"/api/v1/users/{id}", new { Id = id });
        });

        users.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetUserByIdQuery(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        users.MapPost("/{id:guid}/deactivate", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeactivateUserCommand { UserId = id }, ct);
            return Results.NoContent();
        });

        users.MapPost("/{id:guid}/roles", async (Guid id, AssignRoleRequest body, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new AssignUserRoleCommand { UserId = id, RoleName = body.RoleName }, ct);
            return Results.NoContent();
        });

        users.MapDelete("/{id:guid}/roles/{roleName}", async (Guid id, string roleName, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new RevokeUserRoleCommand { UserId = id, RoleName = roleName }, ct);
            return Results.NoContent();
        });

        var roles = app.MapGroup("/roles").RequireAuthorization(p => p.RequireRole("Admin"));

        roles.MapPost("/", async (CreateRoleCommand command, ISender sender, CancellationToken ct) =>
        {
            var id = await sender.Send(command, ct);
            return Results.Created($"/api/v1/roles/{id}", new { Id = id });
        });

        roles.MapGet("/", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetRolesQuery(), ct);
            return Results.Ok(result);
        });

        roles.MapPost("/{roleName}/permissions", async (string roleName, GrantPermissionRequest body, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new GrantPermissionCommand { RoleName = roleName, Permission = body.Permission, ModuleSource = body.ModuleSource }, ct);
            return Results.NoContent();
        });

        roles.MapDelete("/{roleName}/permissions/{permission}", async (string roleName, string permission, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new RevokePermissionCommand { RoleName = roleName, Permission = permission }, ct);
            return Results.NoContent();
        });

        return app;
    }

    private sealed record AssignRoleRequest(string RoleName);

    private sealed record GrantPermissionRequest(string Permission, string ModuleSource);
}
