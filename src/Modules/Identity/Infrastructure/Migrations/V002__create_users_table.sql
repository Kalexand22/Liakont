CREATE TABLE IF NOT EXISTS identity.users (
    id              uuid          NOT NULL DEFAULT gen_random_uuid(),
    username        text          NOT NULL,
    email           text          NOT NULL,
    display_name    text          NOT NULL,
    password_hash   text          NOT NULL,
    party_id        uuid,
    is_active       boolean       NOT NULL DEFAULT true,
    last_login_at   timestamptz,
    created_at      timestamptz   NOT NULL DEFAULT now(),
    updated_at      timestamptz,

    CONSTRAINT pk_users PRIMARY KEY (id),
    CONSTRAINT uq_users_username UNIQUE (username),
    CONSTRAINT uq_users_email UNIQUE (email)
);

CREATE INDEX IF NOT EXISTS ix_users_party_id ON identity.users (party_id) WHERE party_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_users_is_active ON identity.users (is_active) WHERE is_active = true;
