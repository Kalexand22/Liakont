-- Liakont addition (company_id des tenants) - not part of the original Stratum vendoring.
-- Company scope of a tenant (one tenant = one company), fixed at provisioning time.
-- The realm's OIDC client emits it as a hardcoded company_id claim; seed import and
-- user provisioning reuse this value. Nullable: pre-existing tenants keep their
-- externally-managed company_id (dev realm, seeds).
ALTER TABLE outbox.tenants ADD COLUMN company_id uuid;
