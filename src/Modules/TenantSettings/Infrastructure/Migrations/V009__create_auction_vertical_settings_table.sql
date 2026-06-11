-- Activation du vertical « vente aux enchères » du tenant (paramétrage produit, décision opérateur D4
-- du 2026-06-11, lot FIX03). 1 ligne par company_id. Défaut PRODUIT = désactivé (jamais une activation
-- implicite — blueprint §2 règle 7) : une ligne absente vaut « vertical enchères OFF ».
CREATE TABLE IF NOT EXISTS tenantsettings.auction_vertical_settings (
    id          uuid        NOT NULL DEFAULT gen_random_uuid(),
    company_id  uuid        NOT NULL,
    enabled     boolean     NOT NULL DEFAULT false,
    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz,

    CONSTRAINT pk_auction_vertical_settings PRIMARY KEY (id),
    CONSTRAINT uq_auction_vertical_settings_company UNIQUE (company_id)
);
