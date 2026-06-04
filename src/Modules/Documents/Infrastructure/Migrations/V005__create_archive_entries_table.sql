-- Référence en base d'un paquet du coffre d'archive WORM d'un document (F06 §3, item TRK01 — schéma
-- uniquement). La création des paquets, la chaîne de hashes et les addenda chaînés sont portés par le
-- module Archive (TRK05) ; TRK01 crée la table que TRK05 alimente. chain_hash matérialise le chaînage
-- tamper-evident par tenant (TRK05 : chain_hash(N) = SHA256(chain_hash(N-1) + package_hash(N))).
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
