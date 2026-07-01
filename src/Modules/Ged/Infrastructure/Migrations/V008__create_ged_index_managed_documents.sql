-- Entité-pivot d'INDEX du schéma `ged_index` (F19 §3.4.1, item GED03b). `managed_documents` est la fiche
-- d'indexation d'un document géré : son identité primaire est SA PROPRE clé GED, le rattachement fiscal est
-- l'EXCEPTION (un document purement métier n'a aucune contrepartie fiscale). Les rattachements sont des
-- SOFT-LINKS sans FK cross-schéma : `fiscal_document_id` (→ documents.documents.id, optionnel) et
-- `archive_entry_id` (→ documents.archive_entries.id, paquet scellé) — l'index survit à toute évolution du
-- fiscal, et aucune jointure SQL cross-schéma n'est autorisée (INV-GED-08, soft-link logique seulement).
--
-- `content_hash` est SET-ONCE (posé à l'archivage, jamais ré-écrit) : ce n'est qu'une COPIE indexée des octets
-- write-once de IArchiveStore (ancre d'intégrité de référence, option C, INV-ARCH-GED-2). Toute mutation d'une
-- méta-colonne mutable (`title`/`status`/`doc_kind`) est TRACÉE dans `managed_document_change_log` (V010,
-- append-only) — la fiche reste ainsi entièrement auditable.
--
-- PAS de colonne `search_vector` ici (INV-GED-01) : le plein-texte document vit dans la table dérivée et
-- reconstructible `ged_index.document_search` (GED08, F19 §6.3) — foyer UNIQUE, pour éviter une double-source
-- non réconciliable. Base DU TENANT (isolation = la connexion, F19 §3.2) ; aucune colonne tenant_id.
CREATE TABLE IF NOT EXISTS ged_index.managed_documents (
    id                 uuid        NOT NULL,   -- attribué par le handler d'ingestion (INSERT ... ON CONFLICT (id) DO NOTHING, idempotence RL-04)
    title              text        NOT NULL,
    doc_kind           text,                    -- libellé métier libre (PAS un état fiscal)
    fiscal_document_id uuid,                    -- soft-link OPTIONNEL → documents.documents.id (sans FK cross-schéma)
    archive_entry_id   uuid,                    -- soft-link → documents.archive_entries.id (paquet scellé), sans FK
    archive_path       text,                    -- chemin du paquet (chaîne fiscale OU espace '_ged/...' write-once, §5.1)
    content_hash       text,                    -- SHA-256 du contenu indexé (dédup) ; SET-ONCE à l'archivage, jamais ré-écrit
    status             text        NOT NULL DEFAULT 'draft',            -- 'draft'|'indexed'|'archived'|'deferred'
    retention_class    text        NOT NULL DEFAULT 'tenant_bounded',   -- §7 : 'legal_hold'|'tenant_bounded'|'erasable'
    -- PAS de search_vector ici : le FTS document vit dans la table dérivée document_search (§6.3), foyer UNIQUE reconstructible
    created_utc        timestamptz NOT NULL DEFAULT now(),
    updated_utc        timestamptz,
    CONSTRAINT pk_managed_documents PRIMARY KEY (id),
    CONSTRAINT ck_md_status CHECK (status IN ('draft','indexed','archived','deferred')),
    CONSTRAINT ck_md_retention CHECK (retention_class IN ('legal_hold','tenant_bounded','erasable'))
);
CREATE INDEX IF NOT EXISTS ix_md_fiscal  ON ged_index.managed_documents (fiscal_document_id) WHERE fiscal_document_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_md_archive ON ged_index.managed_documents (archive_entry_id)   WHERE archive_entry_id IS NOT NULL;
