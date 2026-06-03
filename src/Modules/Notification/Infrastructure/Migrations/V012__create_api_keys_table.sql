CREATE TABLE IF NOT EXISTS notification.api_keys (
    id          uuid        NOT NULL DEFAULT gen_random_uuid(),
    name        text        NOT NULL,
    key_prefix  text        NOT NULL,
    key_hash    text        NOT NULL,
    scopes      text[]      NOT NULL DEFAULT '{}',
    rate_limit  int         NOT NULL DEFAULT 1000,
    is_revoked  boolean     NOT NULL DEFAULT false,
    company_id  uuid        NOT NULL,
    created_at  timestamptz NOT NULL DEFAULT now(),
    revoked_at  timestamptz,
    expires_at  timestamptz,

    CONSTRAINT pk_api_keys PRIMARY KEY (id)
);

CREATE INDEX ix_api_keys_company_id
    ON notification.api_keys (company_id);

CREATE INDEX ix_api_keys_key_prefix
    ON notification.api_keys (key_prefix);
