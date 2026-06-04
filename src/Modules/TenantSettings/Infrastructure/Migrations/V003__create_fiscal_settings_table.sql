-- Paramétrage fiscal du tenant (F12-A §3). Tous les champs nullables : null = décision en attente
-- = suspension (jamais de défaut). reporting_frequency = chaîne opaque (énumération non figée, §3.3).
CREATE TABLE IF NOT EXISTS tenantsettings.fiscal_settings (
    id                  uuid        NOT NULL DEFAULT gen_random_uuid(),
    company_id          uuid        NOT NULL,
    vat_on_debits       boolean,
    operation_category  int,
    reporting_frequency text,
    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz,

    CONSTRAINT pk_fiscal_settings PRIMARY KEY (id),
    CONSTRAINT uq_fiscal_settings_company UNIQUE (company_id)
);
