# Notification Module

## Purpose

Manages email template definitions with placeholder-based rendering. Provides the foundation for notification sending (email via Job queue), webhook dispatch, and declarative routing of notifications to municipal services based on entity data conditions.

## Type

Engine — exposes `IRoutingEngine` for cross-module consumption via DI.

## Schema

`notification`

## Entities

| Entity | Table | Description |
|--------|-------|-------------|
| EmailTemplate | notification.email_templates | Email template with `{{key}}` placeholder support, category, entityType, templateLinks. Scoped by language and company |
| WebhookSubscription | notification.webhook_subscriptions | Webhook subscription for event-driven dispatch |
| ServiceDefinition | notification.service_definitions | Municipal service definition (code, name, email) |
| RoutingRule | notification.routing_rules | Declarative routing rule with JSON conditions, evaluated against entity data |
| DeliverySla | notification.delivery_sla | SLA definitions per template category (max delay, escalation action) |
| DeliveryRecord | notification.delivery_records | Tracks individual notification delivery: sent, delivered, failed, retry, SLA breach |

## Value Objects

| Name | Description |
|------|-------------|
| RoutingCondition | Tree-structured boolean condition (leaf: field/op/value, compound: and/or). Same format as FormEngine's VisibilityCondition. |
| TemplateLink | Dynamic link in notification templates (label + URL template with placeholders) |

## Domain Services

| Name | Description |
|------|-------------|
| RoutingEvaluator | Evaluates routing rules against entity data, returns matched services ordered by priority |
| RoutingConditionEvaluator | Evaluates individual RoutingCondition trees against a data dictionary |
| TemplateRenderer | Renders templates by replacing `{{key}}` placeholders |
| SlaTracker | Checks delivery records against SLA definitions, detects breaches |

## Cross-module Interfaces

| Interface | Project | Description |
|-----------|---------|-------------|
| IEmailTemplateQueries | Contracts | Query templates by code/language/company |
| IRoutingEngine | Contracts | Evaluate routing rules for an entity type + data → matched services |
| INotificationSender | Contracts | Send email or routed notifications (SendEmailAsync, SendRoutedNotificationsAsync) |
| IRoutingRuleQueries | Contracts | Query routing rules by entity type |
| IServiceDefinitionQueries | Contracts | Query service definitions |
| IDeliverySlaQueries | Contracts | Query SLA definitions by category |
| IDeliveryRecordQueries | Contracts | Query delivery records by entity, SLA breaches, failed for retry |

## Endpoints

| Method | Path | Permission | Description |
|--------|------|------------|-------------|
| POST | /api/notifications/email-templates | notification.template.create | Create email template |
| GET | /api/notifications/email-templates/{code} | notification.template.view | Get template by code |
| GET | /api/notifications/email-templates | notification.template.view | List all templates |
| PUT | /api/notifications/email-templates/{id} | notification.template.update | Update email template |
| POST | /api/notifications/send-email | notification.send | Send email via template |
| POST | /api/notifications/webhooks | notification.webhook.create | Create webhook subscription |
| GET | /api/notifications/webhooks/{id} | notification.webhook.view | Get webhook by ID |
| GET | /api/notifications/webhooks | notification.webhook.view | List webhooks by company |
| PUT | /api/notifications/webhooks/{id} | notification.webhook.update | Update webhook |
| DELETE | /api/notifications/webhooks/{id} | notification.webhook.delete | Delete webhook |
| GET | /api/notifications/services | notification.service.view | List service definitions |
| POST | /api/notifications/services | notification.service.create | Create service definition |
| GET | /api/notifications/routing-rules?entityType= | notification.routing.view | List routing rules by entity type |
| POST | /api/notifications/routing-rules | notification.routing.create | Create routing rule |
| PUT | /api/notifications/routing-rules/{code} | notification.routing.update | Update routing rule |
| POST | /api/notifications/route | notification.routing.view | Evaluate routing rules (dry-run) |
| GET | /api/notifications/delivery-records?entityType=&entityId= | notification.delivery.view | List delivery records for entity |
| GET | /api/notifications/sla-breaches | notification.delivery.view | List SLA-breached delivery records |

## UI Screens

### Admin Pages (`/admin/…`)

| Route | Page | Purpose |
|-------|------|---------|
| `/admin/notifications/templates` | AdminNotificationTemplates | Email templates list (DeclaredListPage) |
| `/admin/notifications/templates/new`, `/{Id}` | AdminNotificationTemplateDetail | Template form with EmailTemplateEditor |
| `/admin/notifications/routing` | AdminNotificationRouting | Routing rules list (DeclaredListPage) |
| `/admin/notifications/routing/new`, `/{Id}`, `/{Id}/edit` | AdminNotificationRoutingDetail | Routing rule form with LogicTreeEditor |
| `/admin/notifications/preview` | AdminNotificationPreview | RoutingPreviewConsole (dry-run simulation) |
| `/admin/catalog/services` | AdminCatalogServices | Municipal service definitions list |
| `/admin/catalog/services/new`, `/{Id}`, `/{Id}/edit` | AdminCatalogServiceForm | Service definition CRUD form |
| `/admin/sla` | AdminSla | SLA configuration + monitoring (3 tabs) |
| `/admin/sla/new`, `/{Id}`, `/{Id}/edit` | AdminSlaForm | SLA definition form |
| `/admin/notifications/webhooks` | AdminWebhookSubscriptions | Webhook subscriptions list (DeclaredListPage) with test-fire, toggle active |
| `/admin/notifications/webhooks/new`, `/{Id}`, `/{Id}/edit` | AdminWebhookForm | Webhook subscription form (name, event type, URL, secret) |
| `/admin/integrations` | AdminIntegrations | API keys + external integration configs (Tourinsoft, ChorusPro, SIG) |

All pages use `@rendermode InteractiveServer`.

### Showcase

| Route | Page | Demonstrates |
|-------|------|-------------|
| `/showcase/components/email-template-editor` | EmailTemplateEditorDemo | WYSIWYG email editor with variable insertion, desktop/mobile preview |
| `/showcase/components/routing-preview-console` | RoutingPreviewConsoleDemo | Dry-run routing simulation with entity data input |

## E2E Test Coverage

| Test File | Scope | Methods |
|-----------|-------|---------|
| AdminNotificationsE2ETests | Templates, routing, preview — CRUD + LogicTreeEditor + a11y | 21 |
| AdminCatalogServicesE2ETests | Municipal service definitions CRUD | 7 |
| AdminSlaE2ETests | SLA configuration and monitoring | 12 |
| NotificationSlaNavE2ETests | SLA sidebar navigation | 2 |
| AdminWebhooksE2ETests | Webhook list + form CRUD, secret toggle, validation, a11y | 7 |

## Integration Events

| Logical Name | C# Type | Description |
|-------------|---------|-------------|
| notification.email.sent | EmailSentV1 | Email successfully sent |
| notification.email.failed | EmailFailedV1 | Email sending failed |
| notification.webhook.dispatched | WebhookDispatchedV1 | Webhook dispatched |
| notification.webhook.dispatch_failed | WebhookDispatchFailedV1 | Webhook dispatch failed |
| notification.delivery.sla_breached | DeliverySlaBreachedV1 | Delivery SLA threshold exceeded |
| notification.routing.routed | RoutingRoutedV1 | Notifications routed to matched services |
