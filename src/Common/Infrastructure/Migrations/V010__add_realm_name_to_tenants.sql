-- Add Keycloak realm tracking to tenant registry
ALTER TABLE outbox.tenants ADD COLUMN realm_name TEXT;
ALTER TABLE outbox.tenants ADD COLUMN client_secret TEXT;

-- Backfill existing tenants using naming convention
UPDATE outbox.tenants
SET realm_name = 'stratum-' || REPLACE(id, '_', '-')
WHERE realm_name IS NULL;

ALTER TABLE outbox.tenants ALTER COLUMN realm_name SET NOT NULL;
ALTER TABLE outbox.tenants ADD CONSTRAINT uq_tenants_realm_name UNIQUE (realm_name);
