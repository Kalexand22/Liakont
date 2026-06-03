ALTER TABLE identity.users
    ADD COLUMN IF NOT EXISTS totp_secret_encrypted bytea,
    ADD COLUMN IF NOT EXISTS is_mfa_enabled boolean NOT NULL DEFAULT false;
