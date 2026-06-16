-- Comptes de signature d'un tenant (ADR-0029 §6 ; patron tenantsettings.pa_accounts). N lignes par
-- company_id (un type de fournisseur × environnement). encrypted_api_key / encrypted_webhook_secret =
-- secrets CHIFFRÉS (Data Protection, purpose versionné côté Host), JAMAIS en clair (CLAUDE.md n°10) ;
-- NULL = aucun secret saisi (placeholder à compléter via la console). account_identifiers = config NON
-- sensible (JSON opaque, ex. workspace). L'URL de base n'est PAS stockée ici : le plug-in la dérive d'une
-- allowlist par environnement (anti-SSRF, ADR-0029 §6). Tenant-scopé par company_id (CLAUDE.md n°9).
CREATE TABLE IF NOT EXISTS signature.signature_provider_accounts (
    id                       uuid        NOT NULL DEFAULT gen_random_uuid(),
    company_id               uuid        NOT NULL,
    provider_type            text        NOT NULL,
    environment              text        NOT NULL,
    account_identifiers      text        NOT NULL DEFAULT '',
    encrypted_api_key        text,
    encrypted_webhook_secret text,
    is_active                boolean     NOT NULL DEFAULT true,
    created_at               timestamptz NOT NULL DEFAULT now(),
    updated_at               timestamptz,

    CONSTRAINT pk_signature_provider_accounts PRIMARY KEY (id),
    -- Un seul compte par (tenant, type de fournisseur) : la clé d'upsert tenant-scopée.
    CONSTRAINT uq_signature_provider_accounts_company_type UNIQUE (company_id, provider_type)
);

CREATE INDEX IF NOT EXISTS ix_signature_provider_accounts_company
    ON signature.signature_provider_accounts (company_id);
