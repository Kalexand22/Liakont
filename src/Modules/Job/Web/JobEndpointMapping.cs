namespace Stratum.Modules.Job.Web;

using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Stratum.Modules.Job.Contracts;
using Stratum.Modules.Job.Contracts.Commands;
using Stratum.Modules.Job.Contracts.Queries;

public static class JobEndpointMapping
{
    private static readonly HashSet<string> ValidStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Pending", "Running", "Completed", "Failed", "Dead",
    };

    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/job");

        MapJobRoutes(group);
        MapScheduleRoutes(group);

        return app;
    }

    private static void MapJobRoutes(RouteGroupBuilder group)
    {
        group.MapGet("/jobs/{id:guid}", async (
            Guid id,
            IJobQueries queries,
            CancellationToken ct) =>
        {
            var result = await queries.GetByIdAsync(id, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization(JobPermissions.View);

        group.MapGet("/jobs", async (
            string status,
            int? limit,
            IJobQueries queries,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return Results.BadRequest("status is required.");
            }

            if (!ValidStatuses.Contains(status))
            {
                return Results.BadRequest($"Invalid status '{status}'. Must be one of: {string.Join(", ", ValidStatuses)}.");
            }

            var result = await queries.ListByStatusAsync(status, limit ?? 50, ct);
            return Results.Ok(result);
        }).RequireAuthorization(JobPermissions.View);
    }

    private static void MapScheduleRoutes(RouteGroupBuilder group)
    {
        group.MapPost("/schedules", async (
            CreateScheduleCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var id = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/job/schedules/{id}", new { id });
        }).RequireAuthorization(JobPermissions.ManageSchedules);

        group.MapGet("/schedules/{id:guid}", async (
            Guid id,
            IScheduleQueries queries,
            CancellationToken ct) =>
        {
            var result = await queries.GetByIdAsync(id, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization(JobPermissions.View);

        group.MapGet("/schedules", async (
            Guid companyId,
            IScheduleQueries queries,
            CancellationToken ct) =>
        {
            var result = await queries.ListByCompanyAsync(companyId, ct);
            return Results.Ok(result);
        }).RequireAuthorization(JobPermissions.View);

        group.MapPut("/schedules/{id:guid}", async (
            Guid id,
            UpdateScheduleRequest request,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var command = new UpdateScheduleCommand
            {
                ScheduleId = id,
                Name = request.Name,
                CronExpression = request.CronExpression,
                JobType = request.JobType,
                PayloadTemplate = request.PayloadTemplate,
            };
            await mediator.Send(command, ct);
            return Results.NoContent();
        }).RequireAuthorization(JobPermissions.ManageSchedules);

        group.MapPost("/schedules/{id:guid}/toggle", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var command = new ToggleScheduleCommand { ScheduleId = id };
            await mediator.Send(command, ct);
            return Results.NoContent();
        }).RequireAuthorization(JobPermissions.ManageSchedules);
    }

    public record UpdateScheduleRequest
    {
        public required string Name { get; init; }

        public required string CronExpression { get; init; }

        public required string JobType { get; init; }

        public string PayloadTemplate { get; init; } = "{}";
    }
}
