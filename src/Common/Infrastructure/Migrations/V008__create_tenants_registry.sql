-- Tenants registry: system-level table tracking all provisioned tenants.
-- Lives in the outbox schema (shared system schema for cross-cutting concerns).
CREATE TABLE IF NOT EXISTS outbox.tenants (
    id              TEXT         PRIMARY KEY,
    display_name    TEXT         NOT NULL,
    admin_email     TEXT         NOT NULL,
    schema_name     TEXT         NOT NULL UNIQUE,
    provisioned_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
    is_active       BOOLEAN      NOT NULL DEFAULT TRUE
);
