CREATE TABLE IF NOT EXISTS identity.user_preferences (
    user_id    uuid          NOT NULL,
    theme      text          NOT NULL DEFAULT 'system',
    language   text          NOT NULL DEFAULT 'fr-FR',
    density    text          NOT NULL DEFAULT 'standard',
    extensions jsonb         NOT NULL DEFAULT '{}'::jsonb,
    updated_at timestamptz   NULL,

    CONSTRAINT pk_user_preferences PRIMARY KEY (user_id),
    CONSTRAINT fk_user_preferences_users FOREIGN KEY (user_id)
        REFERENCES identity.users(id) ON DELETE CASCADE,
    CONSTRAINT ck_user_preferences_theme
        CHECK (theme IN ('light', 'dark', 'system')),
    CONSTRAINT ck_user_preferences_density
        CHECK (density IN ('compact', 'standard')),
    CONSTRAINT ck_user_preferences_extensions_size
        CHECK (octet_length(extensions::text) <= 4096)
);
