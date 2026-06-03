CREATE TABLE IF NOT EXISTS notification.integration_configs (
    id                uuid        NOT NULL DEFAULT gen_random_uuid(),
    integration_type  text        NOT NULL,
    config_json       jsonb       NOT NULL DEFAULT '{}',
    is_enabled        boolean     NOT NULL DEFAULT false,
    company_id        uuid        NOT NULL,
    created_at        timestamptz NOT NULL DEFAULT now(),
    updated_at        timestamptz,

    CONSTRAINT pk_integration_configs PRIMARY KEY (id),
    CONSTRAINT uq_integration_configs_type_company
        UNIQUE (integration_type, company_id)
);
