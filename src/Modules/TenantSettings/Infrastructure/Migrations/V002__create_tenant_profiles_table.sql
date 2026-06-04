-- Profil légal du tenant (F12-A §2). 1 ligne par company_id (scope intra-base ; la base elle-même
-- est par tenant — isolation physique socle Stratum).
CREATE TABLE IF NOT EXISTS tenantsettings.tenant_profiles (
    id                    uuid        NOT NULL DEFAULT gen_random_uuid(),
    company_id            uuid        NOT NULL,
    siren                 text        NOT NULL,
    raison_sociale        text        NOT NULL,
    address_street        text        NOT NULL,
    address_postal_code   text        NOT NULL,
    address_city          text        NOT NULL,
    address_country       text        NOT NULL,
    contact_email_alerte  text,
    statut                int         NOT NULL DEFAULT 0,
    created_at            timestamptz NOT NULL DEFAULT now(),
    updated_at            timestamptz,

    CONSTRAINT pk_tenant_profiles PRIMARY KEY (id),
    CONSTRAINT uq_tenant_profiles_company UNIQUE (company_id)
);
