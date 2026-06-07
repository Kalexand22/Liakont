-- Snapshot requêtable de la ventilation TVA par taux d'un document (ADR-0015, PIP03a). Écrit au CHECK
-- (PIP01b) quand la ventilation SOURCÉE par le mapping validé est calculée ; lu par l'agrégation de
-- paiement APRÈS la purge du staging (ADR-0014). Base DU TENANT (isolation par la connexion ; pas de
-- colonne tenant). APPEND-ONLY, versionné par mapping_version (INV-VENTILATION-003) : une réévaluation
-- (re-mapping) AJOUTE une version, n'écrase jamais. Les lignes {rate, taxable_base, vat_amount} sont
-- stockées en jsonb avec montants/taux en CHAÎNES (sérialisation canonique invariante) — JAMAIS de float
-- (CLAUDE.md n°1) ; le lecteur les reparse en decimal.
CREATE TABLE IF NOT EXISTS pipeline.ventilation_snapshots (
    id                 uuid        NOT NULL DEFAULT gen_random_uuid(),
    document_id        uuid        NOT NULL,
    document_number    text        NOT NULL,
    source_reference   text        NOT NULL,
    operation_category int         NOT NULL,
    mapping_version    text        NOT NULL,
    lines              jsonb       NOT NULL,
    created_utc        timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_ventilation_snapshots PRIMARY KEY (id),
    CONSTRAINT uq_ventilation_snapshots_document_version UNIQUE (document_id, mapping_version)
);

CREATE INDEX IF NOT EXISTS ix_ventilation_snapshots_document_number
    ON pipeline.ventilation_snapshots (document_number);

-- Append-only (INV-VENTILATION-003, CLAUDE.md n°4) : aucune modification/suppression d'une entrée, aucun
-- TRUNCATE — garantie indépendante du code (comme documents.document_events).
CREATE OR REPLACE FUNCTION pipeline.reject_ventilation_snapshot_mutation()
    RETURNS trigger
    LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'Le snapshot de ventilation TVA (pipeline.ventilation_snapshots) est append-only : toute modification ou suppression d''une entrée existante est interdite (ADR-0015, CLAUDE.md n.4).';
END;
$$;

CREATE OR REPLACE TRIGGER trg_ventilation_snapshots_append_only
    BEFORE UPDATE OR DELETE ON pipeline.ventilation_snapshots
    FOR EACH ROW
    EXECUTE FUNCTION pipeline.reject_ventilation_snapshot_mutation();

CREATE OR REPLACE TRIGGER trg_ventilation_snapshots_no_truncate
    BEFORE TRUNCATE ON pipeline.ventilation_snapshots
    FOR EACH STATEMENT
    EXECUTE FUNCTION pipeline.reject_ventilation_snapshot_mutation();
