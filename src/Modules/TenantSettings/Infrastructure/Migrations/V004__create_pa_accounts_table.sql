-- Comptes Plateforme Agréée du tenant (F12-A §4). N lignes par company_id (staging + production,
-- ou multi-PA). encrypted_api_key = clé API CHIFFRÉE (Data Protection), jamais en clair ; NULL =
-- aucune clé saisie (placeholder à compléter via la console).
CREATE TABLE IF NOT EXISTS tenantsettings.pa_accounts (
    id                  uuid        NOT NULL DEFAULT gen_random_uuid(),
    company_id          uuid        NOT NULL,
    plugin_type         text        NOT NULL,
    environment         int         NOT NULL,
    account_identifiers text        NOT NULL DEFAULT '',
    encrypted_api_key   text,
    is_active           boolean     NOT NULL DEFAULT true,
    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz,

    CONSTRAINT pk_pa_accounts PRIMARY KEY (id)
);

CREATE INDEX ix_pa_accounts_company ON tenantsettings.pa_accounts (company_id);
