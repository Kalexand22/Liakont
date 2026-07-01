-- Liens d'axe documentaires — PIÈCE MAÎTRESSE ANTI-EAV du schéma `ged_index` (F19 §3.4.3, item GED03b). Un
-- lien = une valeur d'axe portée par un document géré. Résolution du conflit is_active/append-only : APPEND
-- PUR, aucune colonne mutable (INV-GED-02). La « valeur courante » = la DERNIÈRE de la chaîne de révision
-- (`supersedes_id` posé sur la NOUVELLE ligne, JAMAIS d'UPDATE).
--
-- ANTI-EAV (INV-GED-01) : UNE SEULE colonne de valeur TYPÉE est renseignée selon `axis.data_type` — jamais un
-- `value text` fourre-tout. `value_number` est `numeric` (decimal exact C#), l'échelle étant portée par l'axe
-- (`value_scale`) et appliquée half-up par `ValueNormalizer` AVANT insert — JAMAIS de double/float (règle 1).
--
-- RÉTRACTATION (RL-24) : `is_retraction` retire la valeur courante SANS la remplacer (0 valeur typée +
-- `supersedes_id` obligatoire) ; la contrainte `ck_dal_value_or_retraction` garantit « exactement 1 valeur si
-- lien normal, 0 valeur si rétractation ». Base DU TENANT (isolation = la connexion, F19 §3.2).
CREATE TABLE IF NOT EXISTS ged_index.document_axis_links (
    id                  uuid        NOT NULL DEFAULT gen_random_uuid(),
    seq                 bigint      GENERATED ALWAYS AS IDENTITY,       -- ordre déterministe (monotone, append-compatible)
    managed_document_id uuid        NOT NULL,                           -- → ged_index.managed_documents.id (logique, sans FK)
    axis_id             uuid        NOT NULL,                           -- → ged_catalog.axis_definitions.id (logique, sans FK)
    -- Une seule colonne de valeur typée renseignée selon axis.data_type — JAMAIS un 'value text' fourre-tout :
    value_string        text,
    value_number        numeric,                                       -- decimal exact C# ; échelle portée par l'axe (value_scale)
    value_date          date,
    value_boolean       boolean,
    value_entity_id     uuid,                                          -- → ged_index.entity_instances.id (axe data_type='entity')
    value_json          jsonb,
    normalized_value    text,                                          -- tri/facette/recherche (casefold, unaccent, ISO, decimal canonique)
    source              text        NOT NULL,                          -- 'agent'|'manual'|'ai'|'import'|'ocr'
    confidence_score    numeric,                                       -- [0..1] ; null si déterministe
    supersedes_id       uuid,                                          -- → id de la ligne que celle-ci remplace (chaîne de révision)
    is_retraction       boolean     NOT NULL DEFAULT false,            -- retrait append-only : retire la valeur courante sans la remplacer (RL-24)
    created_utc         timestamptz NOT NULL DEFAULT now(),
    operator_identity   text,                                          -- présent si source='manual'
    CONSTRAINT pk_document_axis_links PRIMARY KEY (id),
    CONSTRAINT ck_dal_source CHECK (source IN ('agent','manual','ai','import','ocr')),
    CONSTRAINT ck_dal_confidence CHECK (confidence_score IS NULL OR (confidence_score BETWEEN 0 AND 1)),
    CONSTRAINT ck_dal_value_or_retraction CHECK (
        -- exactement 1 valeur typée si lien normal ; 0 valeur + supersedes_id obligatoire si rétractation (RL-24) :
        ( NOT is_retraction AND
          (value_string IS NOT NULL)::int + (value_number IS NOT NULL)::int +
          (value_date IS NOT NULL)::int + (value_boolean IS NOT NULL)::int +
          (value_entity_id IS NOT NULL)::int + (value_json IS NOT NULL)::int = 1 )
        OR
        ( is_retraction AND supersedes_id IS NOT NULL AND
          value_string IS NULL AND value_number IS NULL AND value_date IS NULL AND
          value_boolean IS NULL AND value_entity_id IS NULL AND value_json IS NULL )
    )
);

-- « Valeur courante » = lignes non superséedées par aucune autre ; une rétractation n'est PAS une valeur courante :
CREATE OR REPLACE VIEW ged_index.current_axis_links AS
    SELECT d.* FROM ged_index.document_axis_links d
    WHERE d.is_retraction = false
      AND NOT EXISTS (SELECT 1 FROM ged_index.document_axis_links s WHERE s.supersedes_id = d.id);

CREATE INDEX IF NOT EXISTS ix_dal_doc        ON ged_index.document_axis_links (managed_document_id);
CREATE INDEX IF NOT EXISTS ix_dal_supersedes ON ged_index.document_axis_links (supersedes_id) WHERE supersedes_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_dal_axis_norm  ON ged_index.document_axis_links (axis_id, normalized_value);

-- Garde-fou append-only (motif §3.6, déjà en production : documents.reject_archive_entry_mutation). Un TRIGGER
-- s'oppose à TOUT rôle (y compris propriétaire / superuser), contrairement à un REVOKE sans effet sur le
-- propriétaire de la table (CLAUDE.md n°4, INV-GED-02).
CREATE OR REPLACE FUNCTION ged_index.reject_axis_link_mutation()
    RETURNS trigger
    LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'Les liens d''axe GED (ged_index.document_axis_links) sont append-only : une valeur erronée se REMPLACE par une nouvelle ligne chaînée (supersedes_id) ou une rétractation, jamais par UPDATE/DELETE (CLAUDE.md n.4).';
END;
$$;

CREATE OR REPLACE TRIGGER trg_dal_append_only
    BEFORE UPDATE OR DELETE ON ged_index.document_axis_links
    FOR EACH ROW
    EXECUTE FUNCTION ged_index.reject_axis_link_mutation();

-- TRUNCATE ne déclenche PAS un trigger de ligne (FOR EACH ROW) : un trigger d'INSTRUCTION séparé ferme ce
-- vecteur de purge en masse des liens (CLAUDE.md n°4).
CREATE OR REPLACE TRIGGER trg_dal_no_truncate
    BEFORE TRUNCATE ON ged_index.document_axis_links
    FOR EACH STATEMENT
    EXECUTE FUNCTION ged_index.reject_axis_link_mutation();
