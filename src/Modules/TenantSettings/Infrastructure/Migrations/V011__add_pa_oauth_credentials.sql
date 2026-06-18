-- Credentials OAuth2 (client_credentials) d'un compte PA, pour les plug-ins en AuthMode
-- OAuth2ClientCredentials (ex. Super PDP) — F12-A §4. Deux secrets DISTINCTS de encrypted_api_key :
-- client_id et client_secret, CHIFFRÉS (Data Protection, purposes dédiés) — jamais en clair en base,
-- en log ni dans un DTO de lecture (CLAUDE.md n°10, INV-TENANTSETTINGS-003). Nullable : null = non saisi
-- (un compte en ApiKey n'est pas impacté). Idempotent (ADD COLUMN IF NOT EXISTS — patron V008).
ALTER TABLE tenantsettings.pa_accounts
    ADD COLUMN IF NOT EXISTS encrypted_client_id text;

ALTER TABLE tenantsettings.pa_accounts
    ADD COLUMN IF NOT EXISTS encrypted_client_secret text;
