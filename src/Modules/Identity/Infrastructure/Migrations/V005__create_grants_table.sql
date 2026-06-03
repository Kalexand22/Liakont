CREATE TABLE IF NOT EXISTS identity.grants (
    id             uuid          NOT NULL DEFAULT gen_random_uuid(),
    role_id        uuid          NOT NULL,
    permission     text          NOT NULL,
    module_source  text          NOT NULL,
    created_at     timestamptz   NOT NULL DEFAULT now(),

    CONSTRAINT pk_grants PRIMARY KEY (id),
    CONSTRAINT fk_grants_roles FOREIGN KEY (role_id)
        REFERENCES identity.roles(id),
    CONSTRAINT uq_grants_role_permission UNIQUE (role_id, permission)
);

CREATE INDEX IF NOT EXISTS ix_grants_role_id ON identity.grants (role_id);
CREATE INDEX IF NOT EXISTS ix_grants_permission ON identity.grants (permission);
CREATE INDEX IF NOT EXISTS ix_grants_module_source ON identity.grants (module_source);
