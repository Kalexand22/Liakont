-- RTE02: Delivery SLA definitions
CREATE TABLE IF NOT EXISTS notification.delivery_sla (
    id                    uuid            PRIMARY KEY DEFAULT gen_random_uuid(),
    category              smallint        NOT NULL,
    max_delay_seconds     integer         NOT NULL CHECK (max_delay_seconds > 0),
    escalation_action     varchar(200),
    escalation_recipient  varchar(320),
    company_id            uuid,
    created_at            timestamptz     NOT NULL DEFAULT now(),
    updated_at            timestamptz
);

-- Unique SLA per category per company
CREATE UNIQUE INDEX IF NOT EXISTS uq_delivery_sla_category_company
    ON notification.delivery_sla (category, company_id)
    WHERE company_id IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS uq_delivery_sla_category_global
    ON notification.delivery_sla (category)
    WHERE company_id IS NULL;

-- RTE02: Delivery tracking records
CREATE TABLE IF NOT EXISTS notification.delivery_records (
    id                uuid            PRIMARY KEY DEFAULT gen_random_uuid(),
    notification_id   uuid,
    template_code     varchar(100)    NOT NULL,
    recipient_email   varchar(320)    NOT NULL,
    entity_type       varchar(100),
    entity_id         varchar(100),
    sent_at           timestamptz     NOT NULL DEFAULT now(),
    delivered_at      timestamptz,
    failed_at         timestamptz,
    retry_count       integer         NOT NULL DEFAULT 0,
    sla_breached      boolean         NOT NULL DEFAULT false,
    company_id        uuid
);

-- Index for querying delivery records by entity
CREATE INDEX IF NOT EXISTS ix_delivery_records_entity
    ON notification.delivery_records (entity_type, entity_id)
    WHERE entity_type IS NOT NULL;

-- Index for finding failed records for retry
CREATE INDEX IF NOT EXISTS ix_delivery_records_failed
    ON notification.delivery_records (failed_at)
    WHERE failed_at IS NOT NULL AND delivered_at IS NULL;

-- Index for SLA breach queries
CREATE INDEX IF NOT EXISTS ix_delivery_records_sla_breached
    ON notification.delivery_records (sla_breached, company_id)
    WHERE sla_breached = true;
