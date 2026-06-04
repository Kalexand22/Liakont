-- Agrégat jour × taux de l'e-reporting de paiement (F09 §5.3 / F06 §3, item TRK04). Données transmises à la
-- DGFiP : SIREN + période + montant encaissé ventilé par jour et par taux (F09 §2), aucune donnée nominative.
-- TRK04 porte le MODÈLE et la PERSISTANCE ; le CALCUL d'agrégation arrive avec le pipeline (PIP03).
--
-- RÈGLE MONTANTS : taxable_base / vat_amount en NUMERIC(18,2) — JAMAIS de flottant (CLAUDE.md n°1), peuvent
-- être négatifs (rectificatif — F09 §5.4). vat_rate en NUMERIC(6,4) : couvre 20.0000 comme 0.2000 sans perte
-- (l'unité du taux est fixée par PIP03 ; TRK04 le stocke fidèlement). state en TEXTE (lisibilité d'audit).
-- Rétention 10 ans, jamais purgé automatiquement (F06 §6 / F09).
CREATE TABLE IF NOT EXISTS payments.payment_aggregates (
    id              uuid          NOT NULL DEFAULT gen_random_uuid(),
    period          text          NOT NULL,
    aggregate_date  date          NOT NULL,
    vat_rate        numeric(6,4)  NOT NULL,
    taxable_base    numeric(18,2) NOT NULL,
    vat_amount      numeric(18,2) NOT NULL,
    state           text          NOT NULL,
    created_utc     timestamptz   NOT NULL DEFAULT now(),
    last_update_utc timestamptz   NOT NULL DEFAULT now(),

    CONSTRAINT pk_payment_aggregates PRIMARY KEY (id)
);

-- Lectures par période / jour (console, transmission). PAS de contrainte d'unicité (period, date, taux) :
-- un agrégat rejeté est remplacé par un nouvel agrégat recalculé (F09), une unicité casserait ce remplacement.
CREATE INDEX IF NOT EXISTS ix_payment_aggregates_period_date
    ON payments.payment_aggregates (period, aggregate_date);
