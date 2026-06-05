-- Journal des exécutions du pipeline (PIP01 — RunLog). Base DU TENANT (isolation par la connexion ;
-- la base est par tenant). Écrit par PIP01b+ (CHECK/SEND/SYNC) ; lu par GET /runs (API01) et la page
-- Traitements (WEB04). run_type / run_trigger sont persistés par NOM d'énumération.
CREATE TABLE IF NOT EXISTS pipeline.run_logs (
    id                    uuid        NOT NULL DEFAULT gen_random_uuid(),
    run_type              text        NOT NULL,
    run_trigger           text        NOT NULL,
    started_at            timestamptz NOT NULL,
    completed_at          timestamptz,
    documents_processed   int         NOT NULL DEFAULT 0,
    documents_succeeded   int         NOT NULL DEFAULT 0,
    documents_failed      int         NOT NULL DEFAULT 0,
    detail                text,

    CONSTRAINT pk_run_logs PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_run_logs_started_at ON pipeline.run_logs (started_at DESC);
