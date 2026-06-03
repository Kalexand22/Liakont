ALTER TABLE notification.service_definitions
    ADD COLUMN IF NOT EXISTS manager_name     varchar(200),
    ADD COLUMN IF NOT EXISTS default_sla_hours integer,
    ADD COLUMN IF NOT EXISTS color             varchar(20),
    ADD COLUMN IF NOT EXISTS competences       text;
