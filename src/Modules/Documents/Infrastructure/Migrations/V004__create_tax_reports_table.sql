-- Tax reports DGFiP récupérés pour un document (F06 §3, item TRK01 — schéma uniquement). La récupération
-- et la gestion d'état (pending -> retrieved) sont portées par l'item Archive/Ancrage (TRK06) ; TRK01
-- crée la table. Cette table N'EST PAS append-only : un tax report est mis à jour quand son XML/état
-- est récupéré après coup (contrairement à la piste d'audit document_events).
-- pa_tax_report_id / xml_base64 / retrieved_utc : NULL tant que le report n'est pas récupéré.
CREATE TABLE IF NOT EXISTS documents.tax_reports (
    id               uuid        NOT NULL DEFAULT gen_random_uuid(),
    document_id      uuid        NOT NULL,
    pa_tax_report_id text,
    type             text        NOT NULL,
    transport        text        NOT NULL,
    state            text        NOT NULL,
    xml_base64       text,
    retrieved_utc    timestamptz,

    CONSTRAINT pk_tax_reports PRIMARY KEY (id),
    CONSTRAINT fk_tax_reports_document
        FOREIGN KEY (document_id) REFERENCES documents.documents (id)
);

CREATE INDEX IF NOT EXISTS ix_tax_reports_document
    ON documents.tax_reports (document_id);
