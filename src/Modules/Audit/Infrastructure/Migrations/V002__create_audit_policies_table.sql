CREATE TABLE IF NOT EXISTS audit_module.audit_policies (
    id              uuid          NOT NULL DEFAULT gen_random_uuid(),
    entity_type     text          NOT NULL,
    module_source   text          NOT NULL,
    is_enabled      boolean       NOT NULL DEFAULT true,
    tracked_fields  text[]        NOT NULL DEFAULT '{}',
    created_at      timestamptz   NOT NULL DEFAULT now(),
    updated_at      timestamptz,

    CONSTRAINT pk_audit_policies PRIMARY KEY (id),
    CONSTRAINT uq_audit_policies_entity_type UNIQUE (entity_type)
);

CREATE INDEX IF NOT EXISTS ix_audit_policies_entity_type
    ON audit_module.audit_policies (entity_type);

CREATE INDEX IF NOT EXISTS ix_audit_policies_module_source
    ON audit_module.audit_policies (module_source);
