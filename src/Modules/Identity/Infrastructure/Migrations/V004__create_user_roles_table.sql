CREATE TABLE IF NOT EXISTS identity.user_roles (
    id         uuid          NOT NULL DEFAULT gen_random_uuid(),
    user_id    uuid          NOT NULL,
    role_id    uuid          NOT NULL,
    granted_at timestamptz   NOT NULL DEFAULT now(),
    granted_by uuid,

    CONSTRAINT pk_user_roles PRIMARY KEY (id),
    CONSTRAINT fk_user_roles_users FOREIGN KEY (user_id)
        REFERENCES identity.users(id),
    CONSTRAINT fk_user_roles_roles FOREIGN KEY (role_id)
        REFERENCES identity.roles(id),
    CONSTRAINT uq_user_roles UNIQUE (user_id, role_id)
);

CREATE INDEX IF NOT EXISTS ix_user_roles_user_id ON identity.user_roles (user_id);
CREATE INDEX IF NOT EXISTS ix_user_roles_role_id ON identity.user_roles (role_id);
