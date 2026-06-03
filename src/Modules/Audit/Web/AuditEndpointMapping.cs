namespace Stratum.Modules.Audit.Web;

using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Stratum.Modules.Audit.Contracts;
using Stratum.Modules.Audit.Contracts.Commands;
using Stratum.Modules.Audit.Contracts.DTOs;
using Stratum.Modules.Audit.Contracts.Queries;

public static class AuditEndpointMapping
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/audit");

        group.MapGet("/changes", async (
            string entityType,
            string entityId,
            int? page,
            int? pageSize,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var query = new GetFieldChangesQuery
            {
                EntityType = entityType,
                EntityId = entityId,
                Page = page ?? 1,
                PageSize = pageSize ?? 50,
            };
            var result = await mediator.Send(query, ct);
            return Results.Ok(result);
        }).RequireAuthorization();

        group.MapGet("/activities", async (
            string entityType,
            string entityId,
            int? page,
            int? pageSize,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var query = new GetActivitiesQuery
            {
                EntityType = entityType,
                EntityId = entityId,
                Page = page ?? 1,
                PageSize = pageSize ?? 50,
            };
            var result = await mediator.Send(query, ct);
            return Results.Ok(result);
        }).RequireAuthorization();

        group.MapGet("/policies", async (IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetAuditPoliciesQuery(), ct);
            return Results.Ok(result);
        }).RequireAuthorization();

        group.MapGet("/policies/{entityType}", async (
            string entityType,
            IAuditQueries queries,
            CancellationToken ct) =>
        {
            var result = await queries.GetPolicyByEntityType(entityType, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization();

        group.MapPut("/policies/{entityType}", async (
            string entityType,
            SetAuditPolicyRequest body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var command = new SetAuditPolicyCommand
            {
                EntityType = entityType,
                ModuleSource = body.ModuleSource,
                IsEnabled = body.IsEnabled,
                TrackedFields = body.TrackedFields,
            };
            await mediator.Send(command, ct);
            return Results.NoContent();
        }).RequireAuthorization(AuditPermissions.AuditPolicyWrite);

        group.MapDelete("/policies/{entityType}", async (
            string entityType,
            IMediator mediator,
            CancellationToken ct) =>
        {
            await mediator.Send(new DisableAuditPolicyCommand { EntityType = entityType }, ct);
            return Results.NoContent();
        }).RequireAuthorization(AuditPermissions.AuditPolicyWrite);

        return app;
    }

    private sealed record SetAuditPolicyRequest
    {
        public required string ModuleSource { get; init; }

        public required bool IsEnabled { get; init; }

        public required IReadOnlyList<string> TrackedFields { get; init; }
    }
}
