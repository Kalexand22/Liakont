-- Migrate from schema-per-tenant to database-per-tenant:
-- rename the column that stores the tenant's storage identifier.
ALTER TABLE outbox.tenants RENAME COLUMN schema_name TO database_name;
