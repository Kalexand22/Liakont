-- Planification d'extraction du tenant (F12-A §5). 1 ligne par company_id. hours = heures HH:mm.
CREATE TABLE IF NOT EXISTS tenantsettings.extraction_schedules (
    id                uuid        NOT NULL DEFAULT gen_random_uuid(),
    company_id        uuid        NOT NULL,
    hours             text[]      NOT NULL DEFAULT '{}',
    catch_up_on_start boolean     NOT NULL DEFAULT false,
    created_at        timestamptz NOT NULL DEFAULT now(),
    updated_at        timestamptz,

    CONSTRAINT pk_extraction_schedules PRIMARY KEY (id),
    CONSTRAINT uq_extraction_schedules_company UNIQUE (company_id)
);
