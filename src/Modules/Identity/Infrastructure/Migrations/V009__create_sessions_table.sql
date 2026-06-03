CREATE TABLE IF NOT EXISTS identity.sessions (
    id                  uuid          NOT NULL DEFAULT gen_random_uuid(),
    user_id             uuid          NOT NULL,
    refresh_token_hash  text          NOT NULL,
    device_info         text,
    ip_address          text,
    expires_at          timestamptz   NOT NULL,
    revoked_at          timestamptz,
    created_at          timestamptz   NOT NULL DEFAULT now(),

    CONSTRAINT pk_sessions PRIMARY KEY (id),
    CONSTRAINT fk_sessions_users FOREIGN KEY (user_id)
        REFERENCES identity.users(id)
);

CREATE INDEX IF NOT EXISTS ix_sessions_user_id
    ON identity.sessions (user_id);

CREATE INDEX IF NOT EXISTS ix_sessions_user_id_active
    ON identity.sessions (user_id)
    WHERE revoked_at IS NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ix_sessions_refresh_token_hash
    ON identity.sessions (refresh_token_hash);
