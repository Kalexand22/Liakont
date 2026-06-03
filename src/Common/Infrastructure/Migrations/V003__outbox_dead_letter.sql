ALTER TABLE outbox.pending_events
    ADD COLUMN IF NOT EXISTS retry_count integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS last_error   text;

CREATE TABLE IF NOT EXISTS outbox.dead_letter_events
(
    id             uuid        NOT NULL,
    event_type     text        NOT NULL,
    payload        jsonb       NOT NULL,
    correlation_id uuid        NOT NULL,
    module_source  text        NOT NULL,
    version        integer     NOT NULL,
    occurred_at    timestamptz NOT NULL,
    created_at     timestamptz NOT NULL,
    retry_count    integer     NOT NULL,
    last_error     text,
    moved_at       timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_dead_letter_events PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_dead_letter_events_moved_at
    ON outbox.dead_letter_events (moved_at DESC);
