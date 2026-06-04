-- Règles d'une table de mapping TVA (F03 §4.1, item TVA01 §2). Une règle matche sur le code régime
-- source, la part (0=adjudication, 1=frais, 2=autre) et, optionnellement, des flags source (jsonb).
-- rate_value est en NUMERIC (decimal exact, jamais de type flottant — CLAUDE.md n°1 ; round-trip
-- decimal sans perte). rate_value est NULL quand rate_mode = 1 (taux calculé depuis la source).
-- Le doublon (code régime, part) est interdit au niveau base (défense en profondeur, en plus de la
-- validation structurelle applicative — item TVA01 §3).
CREATE TABLE IF NOT EXISTS tvamapping.mapping_rules (
    id                 uuid        NOT NULL DEFAULT gen_random_uuid(),
    table_id           uuid        NOT NULL,
    ordinal            int         NOT NULL,
    source_regime_code text        NOT NULL,
    label              text,
    part               int         NOT NULL,
    source_flags       jsonb,
    category           int         NOT NULL,
    vatex              text,
    note               text,
    rate_mode          int         NOT NULL,
    rate_value         numeric,
    created_at         timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_mapping_rules PRIMARY KEY (id),
    CONSTRAINT fk_mapping_rules_table FOREIGN KEY (table_id)
        REFERENCES tvamapping.mapping_tables (id) ON DELETE CASCADE,
    CONSTRAINT uq_mapping_rules_regime_part UNIQUE (table_id, source_regime_code, part)
);

CREATE INDEX ix_mapping_rules_table ON tvamapping.mapping_rules (table_id);
