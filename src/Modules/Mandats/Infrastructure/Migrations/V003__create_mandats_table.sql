-- Mandats de facturation (F15 §1.5/§2.2, ADR-0022). Calqué sur le gabarit FORT de mapping_tables :
-- validation humaine (validated_by/validated_date NULL = « NON VALIDÉE »). assujettissement_status
-- (statut déclaré, valeur opaque — l'ensemble admis est NON TRANCHÉ F15 §6, donc TEXTE, pas un enum
-- inventé) et contestation_delay (durée déclarée, interval) sont NULLABLE : NULL = décision en attente
-- = 389 suspendu (INV-MANDATS-4, jamais un défaut). revoked_date NULL = non révoqué. Clé métier
-- (company_id, mandant_id, reference). company_id dénormalisé pour le tenant-scoping défensif (n°9).
-- NextValue (numérotation par mandant) est ABSENT ici : MandatSequence est livré par MND05.
CREATE TABLE IF NOT EXISTS mandats.mandats (
    id                     uuid        NOT NULL DEFAULT gen_random_uuid(),
    company_id             uuid        NOT NULL,
    mandant_id             uuid        NOT NULL,
    reference              text        NOT NULL,
    clause_text            text        NOT NULL,
    est_ecrit              boolean     NOT NULL,
    assujettissement_status text,
    contestation_delay     interval,
    validated_by           text,
    validated_date         date,
    revoked_date           timestamptz,
    created_at             timestamptz NOT NULL DEFAULT now(),
    updated_at             timestamptz,

    CONSTRAINT pk_mandats PRIMARY KEY (id),
    CONSTRAINT uq_mandats_company_mandant_reference UNIQUE (company_id, mandant_id, reference),
    CONSTRAINT fk_mandats_mandant FOREIGN KEY (mandant_id) REFERENCES mandats.mandants (id)
);

CREATE INDEX IF NOT EXISTS ix_mandats_company_mandant ON mandats.mandats (company_id, mandant_id);
