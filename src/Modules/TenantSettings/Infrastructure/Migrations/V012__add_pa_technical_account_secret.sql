-- Mot de passe du compte TECHNIQUE additionnel d'un compte PA, pour les plug-ins en AuthMode
-- OAuth2WithTechnicalAccount (ex. Chorus Pro : OAuth2/PISTE + compte technique cpro-account — F18 §2).
-- Secret DISTINCT de encrypted_api_key / encrypted_client_id / encrypted_client_secret : CHIFFRÉ
-- (Data Protection, purpose dédié) — jamais en clair en base, en log ni dans un DTO de lecture
-- (CLAUDE.md n°10, INV-TENANTSETTINGS-003). Le login/email technique (NON secret) voyage dans
-- account_identifiers. Nullable : null = non saisi (un compte en ApiKey / OAuth2 simple n'est pas
-- impacté). Idempotent (ADD COLUMN IF NOT EXISTS — patron V008/V011).
ALTER TABLE tenantsettings.pa_accounts
    ADD COLUMN IF NOT EXISTS encrypted_technical_password text;
