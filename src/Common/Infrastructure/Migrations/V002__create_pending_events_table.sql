CREATE TABLE IF NOT EXISTS outbox.pending_events (
    id             uuid          NOT NULL DEFAULT gen_random_uuid(),
    event_type     text          NOT NULL,
    payload        jsonb         NOT NULL,
    correlation_id uuid          NOT NULL,
    module_source  text          NOT NULL,
    version        integer       NOT NULL,
    occurred_at    timestamptz   NOT NULL,
    created_at     timestamptz   NOT NULL DEFAULT now(),
    processed_at   timestamptz,

    CONSTRAINT pk_pending_events PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_pending_events_processed_at
    ON outbox.pending_events (processed_at)
    WHERE processed_at IS NULL;
