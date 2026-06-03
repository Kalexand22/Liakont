CREATE TABLE IF NOT EXISTS identity.roles (
    id          uuid          NOT NULL DEFAULT gen_random_uuid(),
    name        text          NOT NULL,
    description text,
    is_system   boolean       NOT NULL DEFAULT false,
    created_at  timestamptz   NOT NULL DEFAULT now(),
    updated_at  timestamptz,

    CONSTRAINT pk_roles PRIMARY KEY (id),
    CONSTRAINT uq_roles_name UNIQUE (name)
);

INSERT INTO identity.roles (name, description, is_system)
VALUES
    ('Admin', 'Full system administrator', true),
    ('User', 'Standard user', true)
ON CONFLICT (name) DO NOTHING;
