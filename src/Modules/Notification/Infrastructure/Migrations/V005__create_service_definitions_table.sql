CREATE TABLE IF NOT EXISTS notification.service_definitions (
    id              uuid            PRIMARY KEY DEFAULT gen_random_uuid(),
    code            varchar(100)    NOT NULL,
    name            varchar(200)    NOT NULL,
    email           varchar(320)    NOT NULL,
    description     text,
    is_active       boolean         NOT NULL DEFAULT true,
    company_id      uuid,
    created_at      timestamptz     NOT NULL DEFAULT now(),
    updated_at      timestamptz
);

-- INV-NOTIF-011: ServiceDefinition.code unique (scoped to company)
CREATE UNIQUE INDEX uq_service_definitions_code_company
    ON notification.service_definitions (code, company_id)
    WHERE company_id IS NOT NULL;

CREATE UNIQUE INDEX uq_service_definitions_code_global
    ON notification.service_definitions (code)
    WHERE company_id IS NULL;
