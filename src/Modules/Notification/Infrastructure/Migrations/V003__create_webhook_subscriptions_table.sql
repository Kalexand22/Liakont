CREATE TABLE IF NOT EXISTS notification.webhook_subscriptions (
    id          uuid        NOT NULL DEFAULT gen_random_uuid(),
    event_type  text        NOT NULL,
    target_url  text        NOT NULL,
    secret      text        NOT NULL,
    is_active   boolean     NOT NULL DEFAULT true,
    company_id  uuid        NOT NULL,
    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz,

    CONSTRAINT pk_webhook_subscriptions PRIMARY KEY (id),
    CONSTRAINT ck_webhook_subscriptions_secret_length CHECK (length(secret) >= 32)
);

CREATE INDEX ix_webhook_subscriptions_event_type
    ON notification.webhook_subscriptions (event_type)
    WHERE is_active = true;

CREATE INDEX ix_webhook_subscriptions_company_id
    ON notification.webhook_subscriptions (company_id);
