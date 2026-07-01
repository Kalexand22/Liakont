-- Liens document↔entité GED (F19 §3.4.5, item GED03c). Rattache un managed_document (V008, GED03b) à une
-- instance du registre entity_instances (V012) dans un RÔLE déclaré. APPEND-ONLY, symétrique de
-- entity_relations : un lien erroné se corrige par chaînage (`supersedes_id`) ou rétractation (`is_retraction`
-- + `supersedes_id`, RL-24), jamais par UPDATE/DELETE. La « valeur courante » est la vue
-- current_document_entity_links.
--
-- Références LOGIQUES (managed_document_id → ged_index.managed_documents.id ; entity_id →
-- ged_index.entity_instances.id) : PAS de FK — soft-link append-only, l'intégrité est portée à l'écriture
-- (Application, §3.8). `role` = libellé métier DÉCLARÉ (paramétrage tenant, ex. 'destinataire','site'), jamais
-- un enum figé côté plateforme (règle 7). `relation_type` et `source` = vocabulaires TECHNIQUES fermés
-- (provenance), contraints par CHECK. Base DU TENANT (isolation = la connexion, F19 §3.2).
CREATE TABLE IF NOT EXISTS ged_index.document_entity_links (
    id                  uuid        NOT NULL DEFAULT gen_random_uuid(),
    seq                 bigint      GENERATED ALWAYS AS IDENTITY,   -- ordre déterministe (monotone, append-compatible)
    managed_document_id uuid        NOT NULL,       -- → ged_index.managed_documents.id (logique, sans FK)
    entity_id           uuid        NOT NULL,       -- → ged_index.entity_instances.id (logique, sans FK)
    role                text        NOT NULL,       -- rôle métier déclaré (paramétrage tenant), ex. 'destinataire','site'
    relation_type       text        NOT NULL,       -- provenance : 'direct'|'inferred'|'extracted'|'inherited'
    confidence_score    numeric,                     -- [0..1] ; null si déterministe
    supersedes_id       uuid,                        -- → id du lien que celui-ci remplace (chaîne de dévalidation)
    is_retraction       boolean     NOT NULL DEFAULT false,   -- retrait append-only d'un lien erroné (RL-24)
    source              text        NOT NULL,       -- 'agent'|'manual'|'ai'|'import'|'ocr'
    created_utc         timestamptz NOT NULL DEFAULT now(),
    operator_identity   text,                        -- présent si source='manual'
    CONSTRAINT pk_document_entity_links PRIMARY KEY (id),
    CONSTRAINT ck_del_relation_type CHECK (relation_type IN ('direct','inferred','extracted','inherited')),
    CONSTRAINT ck_del_source CHECK (source IN ('agent','manual','ai','import','ocr')),
    CONSTRAINT ck_del_confidence CHECK (confidence_score IS NULL OR (confidence_score BETWEEN 0 AND 1)),
    CONSTRAINT ck_del_retraction CHECK (NOT is_retraction OR supersedes_id IS NOT NULL)   -- une rétractation désigne ce qu'elle retire (RL-24)
);

CREATE INDEX IF NOT EXISTS ix_del_doc    ON ged_index.document_entity_links (managed_document_id, role);
CREATE INDEX IF NOT EXISTS ix_del_entity ON ged_index.document_entity_links (entity_id, role);

-- Liens doc↔entité COURANTS = ni rétractés ni superséedés (consommés par la traversée §6.4, RL-24).
CREATE OR REPLACE VIEW ged_index.current_document_entity_links AS
    SELECT d.* FROM ged_index.document_entity_links d
    WHERE d.is_retraction = false
      AND NOT EXISTS (SELECT 1 FROM ged_index.document_entity_links s WHERE s.supersedes_id = d.id);

-- Garde-fou append-only (motif §3.6, déjà en production : documents.reject_archive_entry_mutation). Un TRIGGER
-- s'oppose à TOUT rôle (y compris propriétaire / superuser), contrairement à un REVOKE (CLAUDE.md n°4).
CREATE OR REPLACE FUNCTION ged_index.reject_document_entity_link_mutation()
    RETURNS trigger
    LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'Les liens document↔entité GED (ged_index.document_entity_links) sont append-only : un lien erroné se DÉVALIDE par une nouvelle ligne chaînée (supersedes_id) ou une rétractation, jamais par UPDATE/DELETE (CLAUDE.md n.4).';
END;
$$;

CREATE OR REPLACE TRIGGER trg_del_append_only
    BEFORE UPDATE OR DELETE ON ged_index.document_entity_links
    FOR EACH ROW
    EXECUTE FUNCTION ged_index.reject_document_entity_link_mutation();

-- TRUNCATE ne déclenche PAS un trigger de ligne : un trigger d'INSTRUCTION séparé ferme ce vecteur de purge
-- en masse (CLAUDE.md n°4).
CREATE OR REPLACE TRIGGER trg_del_no_truncate
    BEFORE TRUNCATE ON ged_index.document_entity_links
    FOR EACH STATEMENT
    EXECUTE FUNCTION ged_index.reject_document_entity_link_mutation();
