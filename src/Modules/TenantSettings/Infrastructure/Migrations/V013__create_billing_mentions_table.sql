-- Mentions de facturation du tenant (F12-A §3.4, BUG-26). Données de l'entreprise (CGV) portées sur la
-- facture B2B : termes de paiement (BT-20) + mentions légales FR (BR-FR-05 : PMD/PMT/AAB). Tous nullables :
-- null = mention non renseignée (le pipeline bloque au CHECK une facture B2B FR à mention requise absente).
CREATE TABLE IF NOT EXISTS tenantsettings.billing_mentions (
    id                  uuid        NOT NULL DEFAULT gen_random_uuid(),
    company_id          uuid        NOT NULL,
    payment_terms       text,
    late_penalty_terms  text,
    recovery_fee_terms  text,
    discount_terms      text,
    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz,

    CONSTRAINT pk_billing_mentions PRIMARY KEY (id),
    CONSTRAINT uq_billing_mentions_company UNIQUE (company_id)
);
