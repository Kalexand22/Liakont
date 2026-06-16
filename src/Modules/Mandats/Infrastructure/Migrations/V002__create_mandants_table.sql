-- Registre des mandants d'un tenant (F15 §2.2, ADR-0022). Le mandant est un TIERS RÉCURRENT du tenant
-- (jamais un sous-tenant — blueprint §7, INV-MANDATS-1), à forte volumétrie (dizaines de milliers par
-- tenant — F01-F02 R8). Clé métier (company_id, reference) tenant-scopée. Aucune donnée client en dur :
-- raison sociale, n° TVA, SIREN, préfixe sont du PARAMÉTRAGE tenant (CLAUDE.md n°7). Le mandant ne porte
-- AUCUN montant (ADR-0022). seller_vat_number (BT-31) est nullable.
CREATE TABLE IF NOT EXISTS mandats.mandants (
    id                uuid        NOT NULL DEFAULT gen_random_uuid(),
    company_id        uuid        NOT NULL,
    reference         text        NOT NULL,
    raison_sociale    text        NOT NULL,
    seller_vat_number text,
    siren             text        NOT NULL,
    numbering_prefix  text        NOT NULL,
    created_at        timestamptz NOT NULL DEFAULT now(),
    updated_at        timestamptz,

    CONSTRAINT pk_mandants PRIMARY KEY (id),
    CONSTRAINT uq_mandants_company_reference UNIQUE (company_id, reference),
    -- Cible d'une FK COMPOSITE depuis mandats.mandats : garantit qu'un mandat ne référence un mandant
    -- que DANS SON PROPRE tenant (défense en profondeur du tenant-scoping — V003 fk_mandats_mandant).
    CONSTRAINT uq_mandants_company_id UNIQUE (company_id, id)
);

CREATE INDEX IF NOT EXISTS ix_mandants_company ON mandats.mandants (company_id);
