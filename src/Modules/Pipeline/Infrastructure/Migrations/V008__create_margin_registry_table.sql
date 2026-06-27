-- Registre de la MARGE à déclarer (aide à la déclaration de TVA — Livrable 2). Sous le régime de la marge
-- (art. 297 A/E), la TVA ne figure jamais distinctement sur la facture et n'est pas récupérable par l'acheteur :
-- le commissaire-priseur déclare LUI-MÊME la TVA sur sa marge (= commission totale, F03 §2.4/§2.5) dans sa CA3.
-- Cette table est une PROJECTION (read-model) recomposable, peuplée au CHECK quand un document est résolu au
-- régime de la marge : un doc = un taux unique (F03 §2.3 pt 2) → upsert sur document_id. La page console
-- « TVA / Déclaration » l'agrège par mois × taux pour aider à remplir la déclaration.
--
-- Base DU TENANT (isolation par la connexion — database-per-tenant, blueprint §7 ; pas de colonne tenant).
--
-- ⚠️ DISTINCTE du journal d'émission pipeline.b2c_margin_emissions (piste d'audit IMMUABLE des transmissions,
-- WORM, V006) : ICI c'est une PROJECTION RECALCULÉE, NI audit NI WORM (modèle pipeline.payment_aggregations,
-- V004) — recomposée à chaque (re-)CHECK et mise à jour par upsert sur document_id ; un document qui n'est plus
-- au régime de la marge au re-CHECK est SUPPRIMÉ (DELETE applicatif). Aucune contrainte append-only ne s'y
-- applique. Montants en numeric (jamais float — CLAUDE.md n°1) ; taux en numeric(6,4) (parité payment_aggregations).
CREATE TABLE IF NOT EXISTS pipeline.margin_registry (
    document_id     uuid          NOT NULL,
    issue_date      date          NOT NULL,
    currency        text          NOT NULL,
    vat_rate        numeric(6,4)  NOT NULL,
    margin_base_ht  numeric(18,2) NOT NULL,
    margin_vat      numeric(18,2) NOT NULL,
    computed_utc    timestamptz   NOT NULL DEFAULT now(),

    CONSTRAINT pk_margin_registry PRIMARY KEY (document_id)
);

-- Lectures par mois × taux (agrégat de la page « TVA / Déclaration ») : l'index sur le jour d'émission sert le
-- bornage de période [start, endExclusive) (MonthPeriod).
CREATE INDEX IF NOT EXISTS ix_margin_registry_issue_date ON pipeline.margin_registry (issue_date);
