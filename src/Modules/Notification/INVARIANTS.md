# Notification Module — Invariants

| ID | Rule | Enforcement |
|----|------|-------------|
| INV-NOTIF-001 | Template code unique per language per company | UNIQUE constraint (code, language_code, company_id) |
| INV-NOTIF-002 | Template code must not be empty | Domain entity validation |
| INV-NOTIF-003 | Subject template must not be empty | Domain entity validation |
| INV-NOTIF-004 | Body template must not be empty | Domain entity validation |
| INV-NOTIF-005 | Language code must be 2-character ISO 639-1 | Domain entity validation |
| INV-WH-001 | Webhook target URL must be a valid HTTPS URL | Domain entity validation |
| INV-WH-002 | Webhook secret must be at least 32 characters | Domain entity validation |
| INV-WH-003 | Webhook event type must not be empty | Domain entity validation |
| INV-NOTIF-010 | RoutingRule.code unique per entityType (scoped to company) | UNIQUE index (code, entity_type, company_id) |
| INV-NOTIF-011 | ServiceDefinition.code unique (scoped to company) | UNIQUE index (code, company_id) |
| INV-NOTIF-012 | RoutingRule.serviceCode must reference an active ServiceDefinition | Application-level validation |
| INV-NOTIF-020 | TemplateLink label must not be empty | Domain value object validation |
| INV-NOTIF-021 | TemplateLink URL template must not be empty | Domain value object validation |
| INV-NOTIF-030 | DeliverySla maxDelaySeconds must be positive | Domain entity validation |
| INV-NOTIF-031 | DeliveryRecord templateCode must not be empty | Domain entity validation |
| INV-NOTIF-032 | DeliveryRecord recipientEmail must not be empty | Domain entity validation |
