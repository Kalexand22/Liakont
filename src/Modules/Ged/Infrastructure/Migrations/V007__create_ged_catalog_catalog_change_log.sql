-- Journal de changement de la config du catalogue GED (F19 §3.3.4/§3.6, item GED03a). SEULE table
-- APPEND-ONLY du schéma `ged_catalog` : les définitions (entity_types / axis_definitions / axis_values) sont
-- MUTABLES, mais chacune de leurs mutations est TRACÉE ici de façon immuable (piste d'audit de la config).
-- Une correction se fait par une NOUVELLE ligne, jamais par UPDATE/DELETE d'une entrée existante (CLAUDE.md
-- n°4, INV-GED-02). Base DU TENANT (isolation = la connexion, F19 §3.2). change_type reste un texte libre
-- (le vocabulaire des changements de config n'est pas figé — ne pas inventer de contrainte, règle 2) ;
-- axis_id / entity_type_id sont des soft-links (pas de FK : l'audit survit à toute désactivation logique).
CREATE TABLE IF NOT EXISTS ged_catalog.catalog_change_log (
    id                uuid        NOT NULL DEFAULT gen_random_uuid(),
    change_type       text        NOT NULL,       -- ex. 'axis_created','axis_updated','entity_type_created'…
    axis_id           uuid,                        -- soft-link → ged_catalog.axis_definitions.id (sans FK)
    entity_type_id    uuid,                        -- soft-link → ged_catalog.entity_types.id (sans FK)
    before_value      jsonb,
    after_value       jsonb,
    operator_identity text,
    operator_name     text,
    occurred_at       timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_catalog_change_log PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_catalog_change_log_axis
    ON ged_catalog.catalog_change_log (axis_id) WHERE axis_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_catalog_change_log_entity
    ON ged_catalog.catalog_change_log (entity_type_id) WHERE entity_type_id IS NOT NULL;

-- Garde-fou append-only (motif §3.6, déjà en production : documents.reject_archive_entry_mutation). Un
-- TRIGGER s'oppose à TOUT rôle (y compris propriétaire / superuser), contrairement à un REVOKE sans effet
-- sur le propriétaire de la table (CLAUDE.md n°4).
CREATE OR REPLACE FUNCTION ged_catalog.reject_catalog_change_log_mutation()
    RETURNS trigger
    LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'Le journal de config GED (ged_catalog.catalog_change_log) est append-only : une entrée d''audit existante ne peut être ni modifiée ni supprimée (CLAUDE.md n.4).';
END;
$$;

CREATE OR REPLACE TRIGGER trg_catalog_change_log_append_only
    BEFORE UPDATE OR DELETE ON ged_catalog.catalog_change_log
    FOR EACH ROW
    EXECUTE FUNCTION ged_catalog.reject_catalog_change_log_mutation();

-- TRUNCATE ne déclenche PAS un trigger de ligne (FOR EACH ROW) : un trigger d'INSTRUCTION séparé ferme ce
-- vecteur de purge en masse de la piste d'audit (CLAUDE.md n°4).
CREATE OR REPLACE TRIGGER trg_catalog_change_log_no_truncate
    BEFORE TRUNCATE ON ged_catalog.catalog_change_log
    FOR EACH STATEMENT
    EXECUTE FUNCTION ged_catalog.reject_catalog_change_log_mutation();
