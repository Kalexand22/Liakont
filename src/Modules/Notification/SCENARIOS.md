# Notification Module — Scenarios

## S1: Create email template

**Given** no template with code "WELCOME" in language "en" for company X exists
**When** CreateEmailTemplateCommand is sent with code="WELCOME", subject="Welcome {{name}}", body="Hello {{name}}"
**Then** template is created with unique ID and stored in notification.email_templates

## S2: Update email template

**Given** a template with ID exists
**When** UpdateEmailTemplateCommand is sent with new subject and body
**Then** template is updated, updated_at is set

## S3: Unique constraint violation

**Given** a template with code "WELCOME", language "en", company X exists
**When** CreateEmailTemplateCommand is sent with the same code, language, and company
**Then** a unique constraint violation error is returned

## S4: Query template by code

**Given** a template with code "INVOICE" in language "fr" for company X exists
**When** IEmailTemplateQueries.GetByCode("INVOICE", "fr", companyX) is called
**Then** the matching template is returned

## S5: Template rendering

**Given** a template body "Hello {{name}}, your order {{orderId}} is ready"
**When** rendered with placeholders { name: "Alice", orderId: "12345" }
**Then** output is "Hello Alice, your order 12345 is ready"

## S6: Missing placeholder preserved

**Given** a template body "Hello {{name}}, ref {{ref}}"
**When** rendered with placeholders { name: "Bob" }
**Then** output is "Hello Bob, ref {{ref}}" (missing placeholder kept as-is)

## S7: Create webhook subscription

**Given** no webhook subscription exists for event type "sales.quote.created" and company X
**When** CreateWebhookSubscriptionCommand is sent with eventType, targetUrl (HTTPS), secret (32+ chars)
**Then** subscription is created with IsActive=true and stored in notification.webhook_subscriptions

## S8: Update webhook subscription

**Given** a webhook subscription with ID exists
**When** UpdateWebhookSubscriptionCommand is sent with new eventType, targetUrl, and optionally new secret
**Then** subscription is updated, updated_at is set; if secret is null, existing secret is preserved

## S9: Delete webhook subscription

**Given** a webhook subscription with ID exists
**When** DeleteWebhookSubscriptionCommand is sent
**Then** subscription is deleted from the database

## S10: Webhook dispatch — active subscription

**Given** an active webhook subscription exists for a given event
**When** WebhookDispatchJobHandler processes the job payload
**Then** an HTTP POST is sent to the target URL with HMAC signature and event type headers

## S11: Webhook dispatch — inactive subscription

**Given** a webhook subscription exists but IsActive=false
**When** WebhookDispatchJobHandler processes the job payload
**Then** dispatch is skipped with an informational log

## S12: Webhook dispatch — subscription not found

**Given** no webhook subscription exists for the given ID
**When** WebhookDispatchJobHandler processes the job payload
**Then** dispatch is skipped with a warning log

## S13: Create service definition

**Given** no service with code "voirie" exists
**When** CreateServiceDefinitionCommand is sent with code="voirie", name="Service Voirie", email="voirie@commune.fr"
**Then** service definition is created with IsActive=true and stored in notification.service_definitions

## S14: Create routing rule

**Given** a service definition with code "voirie" exists
**When** CreateRoutingRuleCommand is sent with code="voirie-rule", entityType="reservation", serviceCode="voirie", conditions=[{"field":"fermeture_voirie","op":"eq","value":true}]
**Then** routing rule is created and stored in notification.routing_rules

## S15: Evaluate routing — single match

**Given** routing rules exist for entityType "reservation", one matching condition fermeture_voirie=true
**When** IRoutingEngine.EvaluateRoutingAsync("reservation", {"fermeture_voirie": true}) is called
**Then** the voirie service is returned in matches

## S16: Evaluate routing — multiple matches ordered by priority

**Given** routing rules: gestion-salles (priority 10, no conditions), voirie (priority 20, condition matches), communication (priority 30, condition matches)
**When** IRoutingEngine.EvaluateRoutingAsync is called with matching data
**Then** all three are returned ordered by priority: 10, 20, 30

## S17: Evaluate routing — inactive rule skipped

**Given** a routing rule exists but IsActive=false
**When** IRoutingEngine.EvaluateRoutingAsync is called
**Then** the inactive rule is not included in matches

## S18: Evaluate routing — condition not met

**Given** a routing rule with condition fermeture_voirie=true exists
**When** IRoutingEngine.EvaluateRoutingAsync is called with fermeture_voirie=false
**Then** the rule is not included in matches

## S19: Update routing rule

**Given** a routing rule with code "voirie-rule" exists
**When** UpdateRoutingRuleCommand is sent with new priority and conditions
**Then** rule is updated, updated_at is set

## S20: Create delivery SLA

**Given** no SLA for category "transactional" exists
**When** CreateDeliverySlaCommand is sent with category="transactional", maxDelaySeconds=120
**Then** SLA is created and stored in notification.delivery_sla

## S21: Send routed notifications

**Given** routing rules match 2 services for entityType "reservation"
**When** INotificationSender.SendRoutedNotificationsAsync is called with template "reservation-routing"
**Then** 2 DeliveryRecords are created and 2 email jobs are enqueued

## S22: Send routed notifications — no matches

**Given** no routing rules match for the given entity data
**When** INotificationSender.SendRoutedNotificationsAsync is called
**Then** no delivery records or jobs are created, an info log is emitted

## S23: SLA breach detection

**Given** a delivery record sent 10 minutes ago, SLA maxDelaySeconds=120, not yet delivered
**When** SlaTracker.CheckBreach is called
**Then** returns true (breach detected)

## S24: SLA breach — already delivered

**Given** a delivery record marked as delivered
**When** SlaTracker.CheckBreach is called
**Then** returns false (no breach)

## S25: Delivery retry

**Given** a failed delivery record exists with retryCount < max
**When** DeliveryRetryJobHandler processes the retry
**Then** email is re-sent, record is updated to delivered if successful

## S26: Template enrichment — category and links

**Given** a template with category="routing", entityType="reservation", templateLinks=[{label:"Dossier",urlTemplate:"{{URL}}"}]
**When** the template is queried
**Then** category, entityType, and templateLinks are returned in the DTO

## End-to-End Scenarios (Playwright)

| Scenario | Description | Test File |
|----------|-------------|-----------|
| SC-NOTIF-E2E-001 | Templates list page: shows create button, filter bar, and data grid | AdminNotificationsE2ETests |
| SC-NOTIF-E2E-002 | Template create page loads with EmailTemplateEditor | AdminNotificationsE2ETests |
| SC-NOTIF-E2E-003 | Routing list page: loads with routing actions, create button, filter bar | AdminNotificationsE2ETests |
| SC-NOTIF-E2E-004 | Routing create page: shows form sections, LogicTreeEditor, preview console | AdminNotificationsE2ETests |
| SC-NOTIF-E2E-005 | Preview page: loads console with routing link | AdminNotificationsE2ETests |
| SC-NOTIF-E2E-006 | Create routing rule round-trip: form → navigates to detail | AdminNotificationsE2ETests |
| SC-NOTIF-E2E-007 | SLA sidebar navigation visible under notifications | NotificationSlaNavE2ETests |
| SC-NOTIF-E2E-008 | Click SLA link navigates to SLA page | NotificationSlaNavE2ETests |
| SC-NOTIF-E2E-010 | Catalog services list page: create button, data grid | AdminCatalogServicesE2ETests |
| SC-NOTIF-E2E-011 | Service definition CRUD round-trip | AdminCatalogServicesE2ETests |
| SC-NOTIF-E2E-020 | SLA list page loads with configuration tabs | AdminSlaE2ETests |
| SC-NOTIF-E2E-021 | SLA definition CRUD round-trip | AdminSlaE2ETests |
| SC-NOTIF-E2E-022 | SLA monitoring tab shows delivery records | AdminSlaE2ETests |
