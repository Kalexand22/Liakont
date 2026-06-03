-- V011: Add external_id column to users for OIDC provider linking (KC05)
ALTER TABLE identity.users
    ADD COLUMN IF NOT EXISTS external_id VARCHAR(255) NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ix_users_external_id
    ON identity.users (external_id)
    WHERE external_id IS NOT NULL;
