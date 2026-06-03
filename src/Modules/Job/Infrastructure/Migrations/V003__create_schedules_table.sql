CREATE TABLE IF NOT EXISTS job.schedules (
    id                uuid        NOT NULL DEFAULT gen_random_uuid(),
    name              text        NOT NULL,
    cron_expression   text        NOT NULL,
    job_type          text        NOT NULL,
    payload_template  jsonb       NOT NULL DEFAULT '{}'::jsonb,
    is_active         boolean     NOT NULL DEFAULT true,
    next_run_at       timestamptz NOT NULL,
    last_run_at       timestamptz,
    company_id        uuid        NOT NULL,
    created_at        timestamptz NOT NULL DEFAULT now(),
    updated_at        timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_schedules PRIMARY KEY (id),
    CONSTRAINT uq_schedules_name_company UNIQUE (name, company_id)
);

-- Index for scheduler polling: active schedules due for execution
CREATE INDEX ix_schedules_due ON job.schedules (next_run_at)
    WHERE is_active = true;
