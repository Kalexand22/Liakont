namespace Stratum.Modules.Notification.Web;

using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Stratum.Modules.Notification.Contracts;
using Stratum.Modules.Notification.Contracts.Commands;
using Stratum.Modules.Notification.Contracts.Queries;
using Stratum.Modules.Notification.Web.Requests;

public static class NotificationEndpointMapping
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/notifications");

        group.MapPost("/email-templates", async (CreateEmailTemplateCommand command, ISender sender, CancellationToken ct) =>
        {
            var id = await sender.Send(command, ct);
            return Results.Created($"/api/v1/notifications/email-templates/{id}", new { Id = id });
        }).RequireAuthorization(NotificationPermissions.Create);

        group.MapGet("/email-templates/{code}", async (
            string code,
            string? languageCode,
            Guid? companyId,
            IEmailTemplateQueries queries,
            CancellationToken ct) =>
        {
            var result = await queries.GetByCode(code, languageCode ?? "en", companyId, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization(NotificationPermissions.View);

        group.MapGet("/email-templates", async (Guid? companyId, IEmailTemplateQueries queries, CancellationToken ct) =>
        {
            var result = await queries.List(companyId, ct);
            return Results.Ok(result);
        }).RequireAuthorization(NotificationPermissions.View);

        group.MapPut("/email-templates/{id:guid}", async (
            Guid id,
            UpdateEmailTemplateCommand command,
            ISender sender,
            CancellationToken ct) =>
        {
            if (id != command.TemplateId)
            {
                return Results.BadRequest("Route ID does not match body ID.");
            }

            await sender.Send(command, ct);
            return Results.NoContent();
        }).RequireAuthorization(NotificationPermissions.Update);

        group.MapPost("/send-email", async (
            SendEmailRequest request,
            INotificationSender sender,
            CancellationToken ct) =>
        {
            await sender.SendEmailAsync(
                request.TemplateCode,
                request.LanguageCode,
                request.RecipientEmail,
                request.Placeholders ?? new Dictionary<string, string>(),
                request.CompanyId,
                ct);
            return Results.Accepted();
        }).RequireAuthorization(NotificationPermissions.Send);

        // Webhook Subscription endpoints
        group.MapPost("/webhooks", async (CreateWebhookSubscriptionCommand command, ISender sender, CancellationToken ct) =>
        {
            var id = await sender.Send(command, ct);
            return Results.Created($"/api/v1/notifications/webhooks/{id}", new { Id = id });
        }).RequireAuthorization(NotificationPermissions.WebhookCreate);

        group.MapGet("/webhooks/{id:guid}", async (
            Guid id,
            IWebhookQueries queries,
            CancellationToken ct) =>
        {
            var result = await queries.GetById(id, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization(NotificationPermissions.WebhookView);

        group.MapGet("/webhooks", async (Guid? companyId, IWebhookQueries queries, CancellationToken ct) =>
        {
            if (companyId is null)
            {
                return Results.BadRequest("companyId query parameter is required.");
            }

            var result = await queries.ListByCompany(companyId.Value, ct);
            return Results.Ok(result);
        }).RequireAuthorization(NotificationPermissions.WebhookView);

        group.MapPut("/webhooks/{id:guid}", async (
            Guid id,
            UpdateWebhookSubscriptionCommand command,
            ISender sender,
            CancellationToken ct) =>
        {
            if (id != command.SubscriptionId)
            {
                return Results.BadRequest("Route ID does not match body ID.");
            }

            await sender.Send(command, ct);
            return Results.NoContent();
        }).RequireAuthorization(NotificationPermissions.WebhookUpdate);

        group.MapDelete("/webhooks/{id:guid}", async (
            Guid id,
            ISender sender,
            CancellationToken ct) =>
        {
            await sender.Send(new DeleteWebhookSubscriptionCommand { SubscriptionId = id }, ct);
            return Results.NoContent();
        }).RequireAuthorization(NotificationPermissions.WebhookDelete);

        group.MapPost("/webhooks/{id:guid}/test-fire", async (
            Guid id,
            ISender sender,
            Stratum.Common.Abstractions.Security.IActorContextAccessor actorContext,
            CancellationToken ct) =>
        {
            var companyId = actorContext.Current.CompanyId
                ?? throw new InvalidOperationException("No company context available.");
            var result = await sender.Send(new TestFireWebhookCommand { SubscriptionId = id, CompanyId = companyId }, ct);
            return Results.Ok(result);
        }).RequireAuthorization(NotificationPermissions.WebhookUpdate);

        // Service Definition endpoints
        group.MapGet("/services", async (Guid? companyId, IServiceDefinitionQueries queries, CancellationToken ct) =>
        {
            var result = await queries.List(companyId, ct);
            return Results.Ok(result);
        }).RequireAuthorization(NotificationPermissions.ServiceView);

        group.MapPost("/services", async (CreateServiceDefinitionCommand command, ISender sender, CancellationToken ct) =>
        {
            var id = await sender.Send(command, ct);
            return Results.Created($"/api/v1/notifications/services/{id}", new { Id = id });
        }).RequireAuthorization(NotificationPermissions.ServiceCreate);

        // Routing Rule endpoints
        group.MapGet("/routing-rules", async (
            string? entityType,
            Guid? companyId,
            IRoutingRuleQueries queries,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(entityType))
            {
                return Results.BadRequest("entityType query parameter is required.");
            }

            var result = await queries.ListByEntityType(entityType, companyId, ct);
            return Results.Ok(result);
        }).RequireAuthorization(NotificationPermissions.RoutingRuleView);

        group.MapPost("/routing-rules", async (CreateRoutingRuleCommand command, ISender sender, CancellationToken ct) =>
        {
            var id = await sender.Send(command, ct);
            return Results.Created($"/api/v1/notifications/routing-rules/{id}", new { Id = id });
        }).RequireAuthorization(NotificationPermissions.RoutingRuleCreate);

        group.MapPut("/routing-rules/{code}", async (
            string code,
            UpdateRoutingRuleCommand command,
            ISender sender,
            CancellationToken ct) =>
        {
            if (!string.Equals(code, command.Code, StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest("Route code does not match body code.");
            }

            await sender.Send(command, ct);
            return Results.NoContent();
        }).RequireAuthorization(NotificationPermissions.RoutingRuleUpdate);

        group.MapDelete("/routing-rules/{id:guid}", async (
            Guid id,
            ISender sender,
            CancellationToken ct) =>
        {
            await sender.Send(new DeleteRoutingRuleCommand { RoutingRuleId = id }, ct);
            return Results.NoContent();
        }).RequireAuthorization(NotificationPermissions.RoutingRuleDelete);

        // Route evaluation endpoint (dry-run — evaluates rules without sending)
        group.MapPost("/route", async (
            EvaluateRouteRequest request,
            IRoutingEngine routingEngine,
            CancellationToken ct) =>
        {
            var result = await routingEngine.EvaluateRoutingAsync(
                request.EntityType,
                request.Data,
                request.CompanyId,
                ct);
            return Results.Ok(result);
        }).RequireAuthorization(NotificationPermissions.RoutingRuleView);

        // Delivery Record endpoints
        group.MapGet("/delivery-records", async (
            string? entityType,
            string? entityId,
            IDeliveryRecordQueries queries,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(entityType) || string.IsNullOrWhiteSpace(entityId))
            {
                return Results.BadRequest("entityType and entityId query parameters are required.");
            }

            var result = await queries.ListByEntity(entityType, entityId, ct);
            return Results.Ok(result);
        }).RequireAuthorization(NotificationPermissions.DeliveryView);

        // SLA Breaches endpoint
        group.MapGet("/sla-breaches", async (Guid? companyId, IDeliveryRecordQueries queries, CancellationToken ct) =>
        {
            var result = await queries.ListSlaBreaches(companyId, ct);
            return Results.Ok(result);
        }).RequireAuthorization(NotificationPermissions.DeliveryView);

        return app;
    }
}
