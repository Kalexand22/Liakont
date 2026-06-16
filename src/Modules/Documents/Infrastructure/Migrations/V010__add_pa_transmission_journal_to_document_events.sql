-- FX06 — Journalisation de l'envoi à la Plateforme Agréée sur la piste d'audit F06 (§7).
-- Aujourd'hui, l'événement d'émission ne trace que les trois snapshots de preuve (payload, réponse PA,
-- mapping — TRK04). FX06 ajoute, en colonnes EXPLICITES et recherchables, le COMPTE / PLUG-IN de la PA,
-- les HORODATAGES requête/réponse, l'EMPREINTE de l'artefact transmis (le Factur-X) et la CLÉ
-- d'IDEMPOTENCE de l'envoi. La RÉPONSE PA continue d'être portée par la colonne existante
-- `pa_response_snapshot` (jsonb) — aucune colonne doublon n'est créée.
--
-- Toutes les colonnes sont NULLABLE : elles ne valent que pour le nouvel événement
-- `DocumentPaTransmissionJournaled` (les autres événements les laissent NULL). AUCUN backfill UPDATE
-- (modèle V009) — un UPDATE des lignes existantes heurterait le trigger append-only
-- `trg_document_events_append_only`. APPEND-ONLY préservé (CLAUDE.md n°4) : un ALTER TABLE (DDL)
-- ne modifie ni ne supprime aucune entrée et ne déclenche pas le trigger de ligne.
ALTER TABLE documents.document_events
    ADD COLUMN pa_account                text,
    ADD COLUMN pa_plugin_id              text,
    ADD COLUMN pa_request_utc            timestamptz,
    ADD COLUMN pa_response_utc           timestamptz,
    ADD COLUMN transmitted_artifact_hash text,
    ADD COLUMN idempotency_key           text;

-- Clé d'idempotence RECHERCHABLE (F16 §7) : index PARTIEL (seules les lignes journalisées la portent),
-- pour retrouver un envoi par sa clé sans scanner toute la piste d'audit.
CREATE INDEX IF NOT EXISTS ix_document_events_idempotency_key
    ON documents.document_events (idempotency_key)
    WHERE idempotency_key IS NOT NULL;
