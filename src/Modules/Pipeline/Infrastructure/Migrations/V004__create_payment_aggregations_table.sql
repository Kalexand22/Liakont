-- Projection des agrégats jour×taux de l'e-reporting de paiement calculés par PIP03a (F09 §2). Base DU
-- TENANT (isolation par la connexion ; pas de colonne tenant). C'est une PROJECTION RECALCULÉE (read-model),
-- NI une table d'audit NI un coffre WORM : elle est recomposée à chaque exécution de l'agrégateur et mise à
-- jour par upsert sur (aggregate_date, vat_rate) — aucune contrainte append-only ne s'y applique (la piste
-- d'audit immuable des transmissions reste payments.payment_aggregate_events, écrite par PIP03b à l'envoi).
-- Le fenêtrage en période déclarative et l'envoi réel sont PIP03b ; ici il n'y a NI période NI état de
-- transmission, seulement la qualification fiscale (status). Montants en numeric (jamais float — CLAUDE.md n°1).
CREATE TABLE IF NOT EXISTS pipeline.payment_aggregations (
    id             uuid          NOT NULL DEFAULT gen_random_uuid(),
    aggregate_date date          NOT NULL,
    vat_rate       numeric(6,4)  NOT NULL,
    taxable_base   numeric(18,2) NOT NULL,
    vat_amount     numeric(18,2) NOT NULL,
    status         text          NOT NULL,
    reason         text,
    computed_utc   timestamptz   NOT NULL DEFAULT now(),

    CONSTRAINT pk_payment_aggregations PRIMARY KEY (id),
    CONSTRAINT uq_payment_aggregations_date_rate UNIQUE (aggregate_date, vat_rate)
);

CREATE INDEX IF NOT EXISTS ix_payment_aggregations_date ON pipeline.payment_aggregations (aggregate_date);
