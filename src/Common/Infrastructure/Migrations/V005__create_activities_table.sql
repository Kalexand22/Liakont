-- Activity trail for business-level activity logging (SOC_L01).
-- Lives in the shared audit schema alongside field_changes.

CREATE TABLE IF NOT EXISTS audit.activities (
    id            uuid        NOT NULL DEFAULT gen_random_uuid(),
    entity_type   text        NOT NULL,
    entity_id     text        NOT NULL,
    activity_type text        NOT NULL,
    description   text        NOT NULL,
    actor_id      text        NOT NULL,
    metadata      jsonb,
    company_id    uuid,
    created_at    timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_activities PRIMARY KEY (id)
);

CREATE INDEX ix_activities_entity     ON audit.activities (entity_type, entity_id);
CREATE INDEX ix_activities_created_at ON audit.activities (created_at);
