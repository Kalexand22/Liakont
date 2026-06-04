-- Référence en base d'un paquet du coffre d'archive WORM d'un document (F06 §3, item TRK01 — schéma
-- uniquement). La création des paquets, la chaîne de hashes et les addenda chaînés sont portés par le
-- module Archive (TRK05) ; TRK01 crée la table que TRK05 alimente. chain_hash matérialise le chaînage
-- tamper-evident par tenant (TRK05 : chain_hash(N) = SHA256(chain_hash(N-1) + package_hash(N))).
--
-- APPEND-ONLY / WORM : chaque paquet ou addendum est une NOUVELLE ligne — TRK05 ne réécrit jamais une
-- entrée existante. Des triggers REJETTENT tout UPDATE/DELETE et tout TRUNCATE (même discipline que
-- document_events, CLAUDE.md n°4) ; cela ferme toute fenêtre de mutation avant la livraison de TRK05.
CREATE TABLE IF NOT EXISTS documents.archive_entries (
    id            uuid        NOT NULL DEFAULT gen_random_uuid(),
    document_id   uuid        NOT NULL,
    package_path  text        NOT NULL,
    package_hash  text        NOT NULL,
    chain_hash    text        NOT NULL,
    archived_utc  timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_archive_entries PRIMARY KEY (id),
    CONSTRAINT fk_archive_entries_document
        FOREIGN KEY (document_id) REFERENCES documents.documents (id)
);

CREATE INDEX IF NOT EXISTS ix_archive_entries_document
    ON documents.archive_entries (document_id);

-- Garde-fou append-only : un trigger s'applique à TOUT rôle (y compris propriétaire / superuser),
-- contrairement à un REVOKE sans effet sur le propriétaire de la table (CLAUDE.md n°4).
CREATE OR REPLACE FUNCTION documents.reject_archive_entry_mutation()
    RETURNS trigger
    LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'La table documents.archive_entries est write-once (WORM) : toute modification ou suppression d''une entrée existante est interdite (CLAUDE.md n.4).';
END;
$$;

CREATE OR REPLACE TRIGGER trg_archive_entries_append_only
    BEFORE UPDATE OR DELETE ON documents.archive_entries
    FOR EACH ROW
    EXECUTE FUNCTION documents.reject_archive_entry_mutation();

-- TRUNCATE ne déclenche PAS un trigger de ligne (FOR EACH ROW) : un trigger d'INSTRUCTION séparé ferme
-- ce vecteur de purge en masse du coffre d'archive (CLAUDE.md n°4).
CREATE OR REPLACE TRIGGER trg_archive_entries_no_truncate
    BEFORE TRUNCATE ON documents.archive_entries
    FOR EACH STATEMENT
    EXECUTE FUNCTION documents.reject_archive_entry_mutation();
