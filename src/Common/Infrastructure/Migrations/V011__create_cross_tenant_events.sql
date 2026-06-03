-- Cross-Tenant Events outbox table (system database only).
-- Used by the cross-tenant dispatch system to route events between tenants
-- and to accept public submissions (source_tenant IS NULL).
-- Uses inline status lifecycle (pending → delivered / failed → dead) rather than
-- a separate dead-letter table, per the cross-tenant design doc.

CREATE TABLE IF NOT EXISTS outbox.cross_tenant_events (
    id              uuid        NOT NULL DEFAULT gen_random_uuid(),
    source_tenant   text,                          -- NULL = public submission
    target_tenant   text        NOT NULL,
    event_type      text        NOT NULL,          -- convention: {Module}.{Aggregate}.{Verb}
    payload         jsonb       NOT NULL,
    blob_refs       jsonb,                         -- [{storage_key, filename, content_type, size_bytes}]
    submitter_email text,                          -- contact for public submissions
    status          text        NOT NULL DEFAULT 'pending',
    created_at      timestamptz NOT NULL DEFAULT now(),
    delivered_at    timestamptz,
    retry_count     integer     NOT NULL DEFAULT 0,
    last_error      text,

    CONSTRAINT pk_cross_tenant_events PRIMARY KEY (id),
    CONSTRAINT ck_cross_tenant_events_status CHECK (status IN ('pending', 'delivered', 'failed', 'dead')),
    CONSTRAINT ck_cross_tenant_events_event_type_format CHECK (
        event_type ~ '^[A-Z][a-zA-Z]+\.[A-Z][a-zA-Z]+\.[A-Z][a-zA-Z]+$'
    ),
    CONSTRAINT ck_cross_tenant_events_public_has_email CHECK (
        source_tenant IS NOT NULL OR submitter_email IS NOT NULL
    )
);

-- Index for dispatcher polling: pending/failed events ordered by creation time
CREATE INDEX IF NOT EXISTS ix_cte_pending
    ON outbox.cross_tenant_events (status, created_at)
    WHERE status IN ('pending', 'failed');

-- Index for querying events by source tenant
CREATE INDEX IF NOT EXISTS ix_cte_source ON outbox.cross_tenant_events (source_tenant, created_at);

-- Index for querying events by target tenant
CREATE INDEX IF NOT EXISTS ix_cte_target ON outbox.cross_tenant_events (target_tenant, created_at);
