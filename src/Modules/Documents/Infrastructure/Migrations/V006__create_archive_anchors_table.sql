-- Index des ancrages temporels de la tête de chaîne du coffre WORM (F06 « scellement renforcé », item
-- TRK06). Chaque ligne référence l'entrée de coffre (archive_entries) dont le chain_hash a été horodaté et
-- pointe la preuve stockée DANS le coffre (jeton RFC 3161, fichier .ots). La table INDEXE les preuves pour
-- que le vérifieur les retrouve sans énumérer le coffre, comme archive_entries indexe les paquets.
--
-- APPEND-ONLY / WORM : une preuve d'ancrage est une pièce d'audit. Même discipline que document_events et
-- archive_entries (CLAUDE.md n°4) — des triggers REJETTENT tout UPDATE/DELETE et tout TRUNCATE, y compris
-- pour le propriétaire de la table. NoAnchor (instance sans ancrage) ne produit AUCUNE ligne ici.
CREATE TABLE IF NOT EXISTS documents.archive_anchors (
    id                  uuid        NOT NULL DEFAULT gen_random_uuid(),
    chain_head_entry_id uuid        NOT NULL,
    chain_head_hash     text        NOT NULL,
    method              text        NOT NULL,
    status              text        NOT NULL,
    proof_path          text        NULL,
    anchored_utc        timestamptz NULL,
    requested_utc       timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_archive_anchors PRIMARY KEY (id),
    CONSTRAINT fk_archive_anchors_entry
        FOREIGN KEY (chain_head_entry_id) REFERENCES documents.archive_entries (id),
    CONSTRAINT ck_archive_anchors_method
        CHECK (method IN ('none', 'rfc3161', 'opentimestamps')),
    CONSTRAINT ck_archive_anchors_status
        CHECK (status IN ('anchored', 'pending'))
);

-- Clé d'idempotence du job quotidien (ne pas réancrer une tête déjà ancrée par la même méthode).
CREATE INDEX IF NOT EXISTS ix_archive_anchors_head
    ON documents.archive_anchors (chain_head_hash, method);

-- Garde-fou append-only : un trigger s'applique à TOUT rôle (y compris propriétaire / superuser),
-- contrairement à un REVOKE sans effet sur le propriétaire de la table (CLAUDE.md n°4).
CREATE OR REPLACE FUNCTION documents.reject_archive_anchor_mutation()
    RETURNS trigger
    LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'La table documents.archive_anchors est write-once (WORM) : toute modification ou suppression d''un ancrage existant est interdite (CLAUDE.md n.4).';
END;
$$;

CREATE OR REPLACE TRIGGER trg_archive_anchors_append_only
    BEFORE UPDATE OR DELETE ON documents.archive_anchors
    FOR EACH ROW
    EXECUTE FUNCTION documents.reject_archive_anchor_mutation();

-- TRUNCATE ne déclenche PAS un trigger de ligne (FOR EACH ROW) : un trigger d'INSTRUCTION séparé ferme
-- ce vecteur de purge en masse des preuves d'ancrage (CLAUDE.md n°4).
CREATE OR REPLACE TRIGGER trg_archive_anchors_no_truncate
    BEFORE TRUNCATE ON documents.archive_anchors
    FOR EACH STATEMENT
    EXECUTE FUNCTION documents.reject_archive_anchor_mutation();
