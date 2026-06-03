namespace Stratum.Modules.Notification.Contracts;

public static class NotificationPermissions
{
    public const string Create = "notification.template.create";
    public const string Update = "notification.template.update";
    public const string View = "notification.template.view";
    public const string Send = "notification.send";
    public const string WebhookCreate = "notification.webhook.create";
    public const string WebhookUpdate = "notification.webhook.update";
    public const string WebhookView = "notification.webhook.view";
    public const string WebhookDelete = "notification.webhook.delete";

    public const string RoutingRuleCreate = "notification.routing.create";
    public const string RoutingRuleUpdate = "notification.routing.update";
    public const string RoutingRuleView = "notification.routing.view";
    public const string RoutingRuleDelete = "notification.routing.delete";
    public const string ServiceCreate = "notification.service.create";
    public const string ServiceUpdate = "notification.service.update";
    public const string ServiceView = "notification.service.view";
    public const string ServiceDelete = "notification.service.delete";
    public const string DeliveryView = "notification.delivery.view";

    public const string SlaCreate = "notification.sla.create";
    public const string SlaUpdate = "notification.sla.update";
    public const string SlaView = "notification.sla.view";
    public const string SlaDelete = "notification.sla.delete";

    public const string ApiKeyCreate = "notification.apikey.create";
    public const string ApiKeyView = "notification.apikey.view";
    public const string ApiKeyRevoke = "notification.apikey.revoke";
    public const string ApiKeyDelete = "notification.apikey.delete";

    public const string IntegrationView = "notification.integration.view";
    public const string IntegrationUpdate = "notification.integration.update";
}
