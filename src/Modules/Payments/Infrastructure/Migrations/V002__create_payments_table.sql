-- Encaissement BRUT reçu de l'agent (F09 §5.3, item TRK04). Le module conserve le paiement tel qu'extrait
-- par la source, SANS interprétation fiscale (l'agrégation jour × taux arrive avec le pipeline, PIP03).
--
-- RÈGLE MONTANTS : amount en NUMERIC(18,2) — JAMAIS de type flottant (CLAUDE.md n°1). Round-trip base ↔
-- decimal sans perte. Le montant peut être négatif (trop-perçu / remboursement — F09 §5.4).
-- Rétention 10 ans, jamais purgé automatiquement (F06 §6 / F09).
CREATE TABLE IF NOT EXISTS payments.payments (
    id                      uuid        NOT NULL DEFAULT gen_random_uuid(),
    payment_date            date        NOT NULL,
    amount                  numeric(18,2) NOT NULL,
    method                  text,
    related_document_number text,
    source_reference        text,
    received_utc            timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_payments PRIMARY KEY (id)
);

-- L'agrégation (PIP03) somme les paiements par JOUR d'encaissement : index de support.
CREATE INDEX IF NOT EXISTS ix_payments_payment_date
    ON payments.payments (payment_date);
