-- Graphe entité↔entité GED (F19 §3.4.4, item GED03c). Relations de PREMIÈRE CLASSE entre instances du
-- registre entity_instances (V012). APPEND-ONLY : une relation erronée ne se corrige NI par UPDATE NI par
-- DELETE, mais par chaînage — une nouvelle ligne portant `supersedes_id` (dévalidation) OU une rétractation
-- (`is_retraction` + `supersedes_id`, RL-24). La « valeur courante » du graphe est la vue current_entity_relations.
--
-- Références LOGIQUES (from_entity_id / to_entity_id → ged_index.entity_instances.id) : PAS de FK — le graphe
-- est append-only et immuable, il ne peut pas cascader ni bloquer sur une instance ; l'intégrité référentielle
-- est portée à l'écriture (Application, §3.8). `relation_kind` = libellé métier DÉCLARÉ (paramétrage tenant,
-- ex. 'appartient_a','sous_traitant_de'), jamais un enum figé côté plateforme (règle 7). `relation_type` et
-- `source` sont des vocabulaires TECHNIQUES fermés (provenance), donc contraints par CHECK. Base DU TENANT
-- (isolation = la connexion, F19 §3.2).
--
-- CONFIDENTIALITÉ (F19 §6.5/RL-31) : entity_relations ne porte PAS de colonne is_confidential — la
-- confidentialité d'une relation s'HÉRITE des entity_types à ses deux extrémités (prédicat matérialisé dans
-- la traversée §6.4, GED13), jamais dupliquée ici.
CREATE TABLE IF NOT EXISTS ged_index.entity_relations (
    id               uuid        NOT NULL DEFAULT gen_random_uuid(),
    seq              bigint      GENERATED ALWAYS AS IDENTITY,   -- ordre déterministe (monotone, append-compatible)
    from_entity_id   uuid        NOT NULL,        -- → ged_index.entity_instances.id (logique, sans FK)
    to_entity_id     uuid        NOT NULL,        -- → ged_index.entity_instances.id (logique, sans FK)
    relation_kind    text        NOT NULL,        -- libellé métier déclaré (paramétrage tenant), ex. 'appartient_a','sous_traitant_de'
    relation_type    text        NOT NULL,        -- provenance : 'direct'|'inferred'|'extracted'|'inherited'
    confidence_score numeric,                      -- [0..1] ; null si déterministe
    supersedes_id    uuid,                         -- → id de la relation que celle-ci remplace (chaîne de dévalidation)
    is_retraction    boolean     NOT NULL DEFAULT false,   -- retrait append-only d'une relation erronée (RL-24)
    source           text        NOT NULL,        -- 'agent'|'manual'|'ai'|'import'|'ocr'
    created_utc      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_entity_relations PRIMARY KEY (id),
    CONSTRAINT ck_er_relation_type CHECK (relation_type IN ('direct','inferred','extracted','inherited')),
    CONSTRAINT ck_er_source CHECK (source IN ('agent','manual','ai','import','ocr')),
    CONSTRAINT ck_er_no_self CHECK (from_entity_id <> to_entity_id),   -- pas de relation d'une entité vers elle-même
    CONSTRAINT ck_er_confidence CHECK (confidence_score IS NULL OR (confidence_score BETWEEN 0 AND 1)),
    CONSTRAINT ck_er_retraction CHECK (NOT is_retraction OR supersedes_id IS NOT NULL)   -- une rétractation désigne ce qu'elle retire (RL-24)
);

CREATE INDEX IF NOT EXISTS ix_er_from ON ged_index.entity_relations (from_entity_id, relation_kind);
CREATE INDEX IF NOT EXISTS ix_er_to   ON ged_index.entity_relations (to_entity_id, relation_kind);

-- Relations COURANTES = ni rétractées ni superséedées (consommée par la traversée §6.4, RL-24). La courante
-- est un calcul de chaîne (dernière non superséedée), pas une colonne mutable — cohérent avec l'append-only pur.
CREATE OR REPLACE VIEW ged_index.current_entity_relations AS
    SELECT e.* FROM ged_index.entity_relations e
    WHERE e.is_retraction = false
      AND NOT EXISTS (SELECT 1 FROM ged_index.entity_relations s WHERE s.supersedes_id = e.id);

-- Garde-fou append-only (motif §3.6, déjà en production : documents.reject_archive_entry_mutation). Un TRIGGER
-- s'oppose à TOUT rôle (y compris propriétaire / superuser), contrairement à un REVOKE (CLAUDE.md n°4).
CREATE OR REPLACE FUNCTION ged_index.reject_entity_relation_mutation()
    RETURNS trigger
    LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'Le graphe de relations GED (ged_index.entity_relations) est append-only : une relation erronée se DÉVALIDE par une nouvelle ligne chaînée (supersedes_id) ou une rétractation, jamais par UPDATE/DELETE (CLAUDE.md n.4).';
END;
$$;

CREATE OR REPLACE TRIGGER trg_er_append_only
    BEFORE UPDATE OR DELETE ON ged_index.entity_relations
    FOR EACH ROW
    EXECUTE FUNCTION ged_index.reject_entity_relation_mutation();

-- TRUNCATE ne déclenche PAS un trigger de ligne : un trigger d'INSTRUCTION séparé ferme ce vecteur de purge
-- en masse du graphe append-only (CLAUDE.md n°4).
CREATE OR REPLACE TRIGGER trg_er_no_truncate
    BEFORE TRUNCATE ON ged_index.entity_relations
    FOR EACH STATEMENT
    EXECUTE FUNCTION ged_index.reject_entity_relation_mutation();
