-- En-tête de la table de mapping TVA d'un tenant (F03 §4.1, item TVA01). Une table par tenant
-- (contrainte d'unicité sur company_id). validated_by / validated_date NULL = table « NON VALIDÉE »
-- (item TVA01 §5). default_behavior : 0 = block (seule valeur sourcée, F03 §4.1).
CREATE TABLE IF NOT EXISTS tvamapping.mapping_tables (
    id                uuid        NOT NULL DEFAULT gen_random_uuid(),
    company_id        uuid        NOT NULL,
    mapping_version   text        NOT NULL,
    validated_by      text,
    validated_date    date,
    default_behavior  int         NOT NULL,
    created_at        timestamptz NOT NULL DEFAULT now(),
    updated_at        timestamptz,

    CONSTRAINT pk_mapping_tables PRIMARY KEY (id),
    CONSTRAINT uq_mapping_tables_company UNIQUE (company_id)
);
