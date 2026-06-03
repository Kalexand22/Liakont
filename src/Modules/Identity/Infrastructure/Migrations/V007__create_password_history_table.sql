CREATE TABLE IF NOT EXISTS identity.password_history (
    id          uuid          NOT NULL DEFAULT gen_random_uuid(),
    user_id     uuid          NOT NULL,
    hash        text          NOT NULL,
    created_at  timestamptz   NOT NULL DEFAULT now(),

    CONSTRAINT pk_password_history PRIMARY KEY (id),
    CONSTRAINT fk_password_history_users FOREIGN KEY (user_id)
        REFERENCES identity.users(id)
);

CREATE INDEX IF NOT EXISTS ix_password_history_user_id
    ON identity.password_history (user_id);

CREATE INDEX IF NOT EXISTS ix_password_history_user_created
    ON identity.password_history (user_id, created_at DESC);
