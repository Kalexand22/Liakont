-- Journal de changement des instances d'entité GED (F19 §3.4.2/§3.6, item GED03c). entity_instances (V012)
-- est MUTABLE (registre vivant : fusion de doublons, désactivation logique, ré-indexation FTS), mais chacune
-- de ses mutations est TRACÉE ici de façon immuable (piste d'audit du graphe). Une correction se fait par une
-- NOUVELLE ligne, jamais par UPDATE/DELETE d'une entrée existante (CLAUDE.md n°4, INV-GED-02). Base DU TENANT
-- (isolation = la connexion, F19 §3.2). Calqué sur ged_catalog.catalog_change_log (V007) : change_type reste
-- un texte libre (le vocabulaire des changements n'est pas figé — ne pas inventer de contrainte, règle 2) ;
-- entity_instance_id est un soft-link (pas de FK : l'audit survit à toute fusion / désactivation logique).
CREATE TABLE IF NOT EXISTS ged_index.entity_instance_change_log (
    id                 uuid        NOT NULL DEFAULT gen_random_uuid(),
    change_type        text        NOT NULL,       -- ex. 'entity_created','entity_renamed','entity_merged','entity_deactivated'…
    entity_instance_id uuid,                        -- soft-link → ged_index.entity_instances.id (sans FK)
    before_value       jsonb,
    after_value        jsonb,
    operator_identity  text,
    operator_name      text,
    occurred_at        timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_entity_instance_change_log PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_entity_instance_change_log_entity
    ON ged_index.entity_instance_change_log (entity_instance_id) WHERE entity_instance_id IS NOT NULL;

-- Garde-fou append-only (motif §3.6, déjà en production : documents.reject_archive_entry_mutation). Un TRIGGER
-- s'oppose à TOUT rôle (y compris propriétaire / superuser), contrairement à un REVOKE sans effet sur le
-- propriétaire de la table (CLAUDE.md n°4).
CREATE OR REPLACE FUNCTION ged_index.reject_entity_instance_change_log_mutation()
    RETURNS trigger
    LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'Le journal des instances d''entité GED (ged_index.entity_instance_change_log) est append-only : une entrée d''audit existante ne peut être ni modifiée ni supprimée (CLAUDE.md n.4).';
END;
$$;

CREATE OR REPLACE TRIGGER trg_entity_instance_change_log_append_only
    BEFORE UPDATE OR DELETE ON ged_index.entity_instance_change_log
    FOR EACH ROW
    EXECUTE FUNCTION ged_index.reject_entity_instance_change_log_mutation();

-- TRUNCATE ne déclenche PAS un trigger de ligne (FOR EACH ROW) : un trigger d'INSTRUCTION séparé ferme ce
-- vecteur de purge en masse de la piste d'audit (CLAUDE.md n°4).
CREATE OR REPLACE TRIGGER trg_entity_instance_change_log_no_truncate
    BEFORE TRUNCATE ON ged_index.entity_instance_change_log
    FOR EACH STATEMENT
    EXECUTE FUNCTION ged_index.reject_entity_instance_change_log_mutation();
