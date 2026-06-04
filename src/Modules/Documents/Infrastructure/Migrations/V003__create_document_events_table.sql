-- Piste d'audit IMMUABLE d'un document (F06 §3, item TRK01) — cœur de la non-altération (DR6 point 2).
-- APPEND-ONLY : aucun chemin d'update/delete applicatif ET des triggers REJETTENT tout UPDATE/DELETE
-- d'une entrée existante ainsi que tout TRUNCATE de la table (même discipline que mapping_change_log,
-- CLAUDE.md n°4 : aucune purge d'une table d'audit). La garantie est au niveau base (vérifiée par test),
-- indépendante du code applicatif.
--
-- event_type : libellé textuel (DocumentEventType, F06 §3) — lisible pour un contrôle fiscal.
-- payload_snapshot / pa_response_snapshot / mapping_trace : preuve d'un document émis (alimentés par
-- TRK04) ; NULL à la genèse. operator_identity : identité Keycloak pour une action opérateur, NULL pour
-- un événement système (ingestion).
CREATE TABLE IF NOT EXISTS documents.document_events (
    id                   uuid        NOT NULL DEFAULT gen_random_uuid(),
    document_id          uuid        NOT NULL,
    timestamp_utc        timestamptz NOT NULL DEFAULT now(),
    event_type           text        NOT NULL,
    detail               text,
    payload_snapshot     jsonb,
    pa_response_snapshot jsonb,
    mapping_trace        jsonb,
    operator_identity    text,

    CONSTRAINT pk_document_events PRIMARY KEY (id),
    -- Intégrité référentielle : tout événement se rattache à un document existant. Pas de cascade : un
    -- document n'est jamais supprimé (rétention 10 ans, aucune purge — F06 §6), et un éventuel DELETE de
    -- document serait de toute façon bloqué tant que des événements le référencent (défense en profondeur).
    CONSTRAINT fk_document_events_document
        FOREIGN KEY (document_id) REFERENCES documents.documents (id)
);

CREATE INDEX IF NOT EXISTS ix_document_events_document
    ON documents.document_events (document_id, timestamp_utc);

-- Garde-fou append-only : un trigger s'applique à TOUT rôle (y compris propriétaire / superuser),
-- contrairement à un REVOKE sans effet sur le propriétaire de la table (CLAUDE.md n°4).
CREATE OR REPLACE FUNCTION documents.reject_document_event_mutation()
    RETURNS trigger
    LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'La piste d''audit des documents (documents.document_events) est append-only : toute modification ou suppression d''une entrée existante est interdite (CLAUDE.md n.4).';
END;
$$;

CREATE OR REPLACE TRIGGER trg_document_events_append_only
    BEFORE UPDATE OR DELETE ON documents.document_events
    FOR EACH ROW
    EXECUTE FUNCTION documents.reject_document_event_mutation();

-- TRUNCATE ne déclenche PAS un trigger de ligne (FOR EACH ROW) : un trigger d'INSTRUCTION séparé ferme
-- ce vecteur de purge en masse du journal d'audit (CLAUDE.md n°4).
CREATE OR REPLACE TRIGGER trg_document_events_no_truncate
    BEFORE TRUNCATE ON documents.document_events
    FOR EACH STATEMENT
    EXECUTE FUNCTION documents.reject_document_event_mutation();
