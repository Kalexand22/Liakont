CREATE TABLE IF NOT EXISTS notification.routing_rules (
    id              uuid            PRIMARY KEY DEFAULT gen_random_uuid(),
    code            varchar(100)    NOT NULL,
    name            varchar(200)    NOT NULL,
    entity_type     varchar(100)    NOT NULL,
    service_code    varchar(100)    NOT NULL,
    recipient_type  smallint        NOT NULL DEFAULT 0,
    recipient_value varchar(320)    NOT NULL,
    conditions      jsonb           NOT NULL DEFAULT '[]'::jsonb,
    priority        integer         NOT NULL DEFAULT 0,
    is_active       boolean         NOT NULL DEFAULT true,
    company_id      uuid,
    created_at      timestamptz     NOT NULL DEFAULT now(),
    updated_at      timestamptz
);

-- INV-NOTIF-010: RoutingRule.code unique per entityType (scoped to company)
CREATE UNIQUE INDEX uq_routing_rules_code_entity_company
    ON notification.routing_rules (code, entity_type, company_id)
    WHERE company_id IS NOT NULL;

CREATE UNIQUE INDEX uq_routing_rules_code_entity_global
    ON notification.routing_rules (code, entity_type)
    WHERE company_id IS NULL;

-- Lookup index for routing evaluation
CREATE INDEX ix_routing_rules_entity_active
    ON notification.routing_rules (entity_type, is_active)
    WHERE is_active = true;

-- INV-NOTIF-012: service_code validity enforced at application level.
