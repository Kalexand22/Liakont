CREATE TABLE IF NOT EXISTS identity.password_policies (
    id               uuid          NOT NULL DEFAULT gen_random_uuid(),
    company_id       uuid          NULL,
    min_length       int           NOT NULL DEFAULT 8,
    require_uppercase boolean      NOT NULL DEFAULT false,
    require_digit    boolean       NOT NULL DEFAULT false,
    require_special  boolean       NOT NULL DEFAULT false,
    expiration_days  int           NOT NULL DEFAULT 0,
    history_count    int           NOT NULL DEFAULT 0,
    created_at       timestamptz   NOT NULL DEFAULT now(),
    updated_at       timestamptz   NULL,

    CONSTRAINT pk_password_policies PRIMARY KEY (id),
    CONSTRAINT uq_password_policies_company UNIQUE (company_id)
);

CREATE INDEX IF NOT EXISTS ix_password_policies_company_id
    ON identity.password_policies (company_id);
