-- Journal de changement des méta-colonnes d'un document géré (F19 §3.4.1, item GED03b). Table APPEND-ONLY du
-- schéma `ged_index` : `managed_documents` est MUTABLE sur ses méta-colonnes (`title`/`status`/`doc_kind`),
-- mais chacune de ces mutations est TRACÉE ici de façon immuable — le claim d'auditabilité (§3.5) couvre alors
-- les LIENS (document_axis_links) ET l'ENTITÉ-PIVOT. Une correction se fait par une NOUVELLE ligne, jamais par
-- UPDATE/DELETE d'une entrée existante (CLAUDE.md n°4, INV-GED-02).
--
-- `change_type` reste un texte libre (le vocabulaire des changements n'est pas figé — ne pas inventer de
-- contrainte, règle 2) ; `managed_document_id` est un soft-link (pas de FK : l'audit survit à toute évolution
-- du document). Base DU TENANT (isolation = la connexion, F19 §3.2).
CREATE TABLE IF NOT EXISTS ged_index.managed_document_change_log (
    id                  uuid        NOT NULL DEFAULT gen_random_uuid(),
    managed_document_id uuid        NOT NULL,       -- soft-link → ged_index.managed_documents.id (sans FK)
    change_type         text        NOT NULL,       -- ex. 'status_changed','title_updated','doc_kind_updated'…
    before_value        jsonb,
    after_value         jsonb,
    operator_identity   text,
    operator_name       text,
    occurred_at         timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_managed_document_change_log PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_md_change_log_document
    ON ged_index.managed_document_change_log (managed_document_id);

-- Garde-fou append-only (motif §3.6, calqué sur ged_catalog.reject_catalog_change_log_mutation) : opposable à
-- TOUT rôle par un TRIGGER (un REVOKE resterait sans effet sur le propriétaire de la table, CLAUDE.md n°4).
CREATE OR REPLACE FUNCTION ged_index.reject_managed_document_change_log_mutation()
    RETURNS trigger
    LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'Le journal de changement des documents GED (ged_index.managed_document_change_log) est append-only : une entrée d''audit existante ne peut être ni modifiée ni supprimée (CLAUDE.md n.4).';
END;
$$;

CREATE OR REPLACE TRIGGER trg_md_change_log_append_only
    BEFORE UPDATE OR DELETE ON ged_index.managed_document_change_log
    FOR EACH ROW
    EXECUTE FUNCTION ged_index.reject_managed_document_change_log_mutation();

-- TRUNCATE ne déclenche PAS un trigger de ligne (FOR EACH ROW) : un trigger d'INSTRUCTION séparé ferme ce
-- vecteur de purge en masse de la piste d'audit (CLAUDE.md n°4).
CREATE OR REPLACE TRIGGER trg_md_change_log_no_truncate
    BEFORE TRUNCATE ON ged_index.managed_document_change_log
    FOR EACH STATEMENT
    EXECUTE FUNCTION ged_index.reject_managed_document_change_log_mutation();
