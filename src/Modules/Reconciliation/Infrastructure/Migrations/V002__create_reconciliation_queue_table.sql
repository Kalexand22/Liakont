-- File d'attente de réconciliation PDF ↔ documents émis (item TRK07, F06 §7 §3). Une entrée par PDF du
-- pool découvert et traité : rapproché automatiquement (confiance haute), proposé (confiance moyenne, en
-- attente de confirmation opérateur), ou orphelin (aucune correspondance / ambiguïté).
--
-- Table MUTABLE (un orphelin/une proposition peut devenir ReconciledManual après confirmation) — à
-- DISTINGUER de la piste d'audit append-only (documents.document_events). La preuve DÉFINITIVE d'un
-- rapprochement reste le DocumentEvent (Documents) + l'addendum d'archive WORM (Archive) ; cette table
-- est l'état OPÉRATIONNEL consommé par la console (API04/WEB08).
--
-- proposed_document_id référence logiquement un document émis (documents.documents) SANS clé étrangère :
-- une FK cross-schéma coupletterait le module Reconciliation au schéma du module Documents (frontière
-- inter-modules — module-rules §3). Le lien d'intégrité reste l'addendum d'archive + le DocumentEvent.
CREATE TABLE IF NOT EXISTS reconciliation.reconciliation_queue (
    id                   uuid        NOT NULL DEFAULT gen_random_uuid(),
    pool_pdf_id          text        NOT NULL,
    file_name            text        NOT NULL,
    status               text        NOT NULL,
    proposed_document_id uuid,
    strategy             text,
    confidence           text,
    detail               text        NOT NULL DEFAULT '',
    created_utc          timestamptz NOT NULL DEFAULT now(),
    resolved_utc         timestamptz,
    operator_identity    text,

    CONSTRAINT pk_reconciliation_queue PRIMARY KEY (id),
    -- Un PDF du pool n'est traité qu'UNE fois (idempotence de la passe au niveau base).
    CONSTRAINT uq_reconciliation_queue_pool_pdf UNIQUE (pool_pdf_id)
);

CREATE INDEX IF NOT EXISTS ix_reconciliation_queue_status
    ON reconciliation.reconciliation_queue (status, created_utc);
