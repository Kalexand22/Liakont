-- V012: Create crossdata.inbound_pings table for cross-tenant ping/health-check events.
-- This table stores delivered Test.Ping events and proves the cross-tenant dispatch pipeline
-- works end-to-end. Each tenant DB receives its own copy of delivered pings.
-- NOTE: This migration runs on BOTH system and tenant DBs. It is NOT in SystemOnlyMigrationPrefixes,
-- so TenantProvisioningService.RunTenantMigrationsAsync applies it during tenant provisioning.

CREATE SCHEMA IF NOT EXISTS crossdata;

CREATE TABLE IF NOT EXISTS crossdata.inbound_pings (
    id              uuid        NOT NULL DEFAULT gen_random_uuid(),
    event_id        uuid        NOT NULL,
    source_tenant   text,                          -- NULL = public submission
    message         text        NOT NULL,
    submitter_email text,                          -- contact for public submissions
    received_at     timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_inbound_pings PRIMARY KEY (id),
    CONSTRAINT uq_inbound_pings_event_id UNIQUE (event_id)
);
