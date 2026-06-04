-- Piste d'audit IMMUABLE d'un agrégat de paiement (F06 §3 / F09, item TRK04) — même discipline que
-- documents.document_events : un agrégat transmis à la DGFiP a la même valeur fiscale qu'un document émis.
-- APPEND-ONLY : aucun chemin d'update/delete applicatif ET des triggers REJETTENT tout UPDATE/DELETE d'une
-- entrée existante ainsi que tout TRUNCATE de la table (CLAUDE.md n°4 : aucune purge d'une table d'audit).
-- La garantie est au niveau base (vérifiée par test), indépendante du code applicatif.
--
-- event_type / state : libellés textuels (PaymentAggregateEventType / PaymentAggregateState) — lisibles pour
-- un contrôle fiscal. payload_snapshot / pa_response_snapshot : preuve d'une transmission (NULL pour un simple
-- changement d'état : Sending, erreur technique sans réponse PA).
CREATE TABLE IF NOT EXISTS payments.payment_aggregate_events (
    id                   uuid        NOT NULL DEFAULT gen_random_uuid(),
    aggregate_id         uuid        NOT NULL,
    timestamp_utc        timestamptz NOT NULL DEFAULT now(),
    event_type           text        NOT NULL,
    state                text        NOT NULL,
    detail               text,
    payload_snapshot     jsonb,
    pa_response_snapshot jsonb,

    CONSTRAINT pk_payment_aggregate_events PRIMARY KEY (id),
    -- Intégrité référentielle : tout événement se rattache à un agrégat existant. Pas de cascade : un agrégat
    -- n'est jamais supprimé (rétention 10 ans, aucune purge — F06 §6).
    CONSTRAINT fk_payment_aggregate_events_aggregate
        FOREIGN KEY (aggregate_id) REFERENCES payments.payment_aggregates (id)
);

CREATE INDEX IF NOT EXISTS ix_payment_aggregate_events_aggregate
    ON payments.payment_aggregate_events (aggregate_id, timestamp_utc);

-- Garde-fou append-only : un trigger s'applique à TOUT rôle (y compris propriétaire / superuser),
-- contrairement à un REVOKE sans effet sur le propriétaire de la table (CLAUDE.md n°4).
CREATE OR REPLACE FUNCTION payments.reject_payment_aggregate_event_mutation()
    RETURNS trigger
    LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'La piste d''audit des agrégats de paiement (payments.payment_aggregate_events) est append-only : toute modification ou suppression d''une entrée existante est interdite (CLAUDE.md n.4).';
END;
$$;

CREATE OR REPLACE TRIGGER trg_payment_aggregate_events_append_only
    BEFORE UPDATE OR DELETE ON payments.payment_aggregate_events
    FOR EACH ROW
    EXECUTE FUNCTION payments.reject_payment_aggregate_event_mutation();

-- TRUNCATE ne déclenche PAS un trigger de ligne (FOR EACH ROW) : un trigger d'INSTRUCTION séparé ferme
-- ce vecteur de purge en masse du journal d'audit (CLAUDE.md n°4).
CREATE OR REPLACE TRIGGER trg_payment_aggregate_events_no_truncate
    BEFORE TRUNCATE ON payments.payment_aggregate_events
    FOR EACH STATEMENT
    EXECUTE FUNCTION payments.reject_payment_aggregate_event_mutation();
