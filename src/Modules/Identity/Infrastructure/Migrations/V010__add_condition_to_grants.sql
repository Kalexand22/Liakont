ALTER TABLE identity.grants
    ADD COLUMN IF NOT EXISTS condition text;
