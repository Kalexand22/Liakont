CREATE TABLE IF NOT EXISTS job.jobs (
    id            uuid        NOT NULL DEFAULT gen_random_uuid(),
    type          text        NOT NULL,
    payload       jsonb       NOT NULL,
    status        text        NOT NULL DEFAULT 'Pending',
    priority      int         NOT NULL DEFAULT 0,
    max_retries   int         NOT NULL DEFAULT 3,
    retry_count   int         NOT NULL DEFAULT 0,
    scheduled_at  timestamptz NOT NULL DEFAULT now(),
    started_at    timestamptz,
    completed_at  timestamptz,
    error_message text,
    company_id    uuid,
    created_at    timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_jobs PRIMARY KEY (id),
    CONSTRAINT ck_jobs_status_valid CHECK (status IN ('Pending', 'Running', 'Completed', 'Failed', 'Dead'))
);

-- Polling index for SELECT ... FOR UPDATE SKIP LOCKED
CREATE INDEX ix_jobs_pending ON job.jobs (priority DESC, created_at)
    WHERE status = 'Pending';
