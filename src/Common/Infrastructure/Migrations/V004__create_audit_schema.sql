-- SOC_F07: Audit — field-level change table
-- entry_id groups all field changes from a single business operation.

CREATE SCHEMA IF NOT EXISTS audit;

CREATE TABLE IF NOT EXISTS audit.field_changes
(
    id          uuid        NOT NULL DEFAULT gen_random_uuid(),
    entry_id    uuid        NOT NULL,
    entity_type text        NOT NULL,
    entity_id   text        NOT NULL,
    field_name  text        NOT NULL,
    old_value   jsonb,
    new_value   jsonb,
    actor_id    text        NOT NULL,
    occurred_at timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_field_changes PRIMARY KEY (id)
);

-- Fast lookup of all changes within one operation
CREATE INDEX IF NOT EXISTS ix_field_changes_entry_id
    ON audit.field_changes (entry_id);

-- Fast lookup of change history for a given entity (most-recent first)
CREATE INDEX IF NOT EXISTS ix_field_changes_entity
    ON audit.field_changes (entity_type, entity_id, occurred_at DESC);
