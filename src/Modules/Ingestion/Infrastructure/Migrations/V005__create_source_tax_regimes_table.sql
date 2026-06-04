-- Régimes de TVA source observés par tenant (métadonnée de push, F12, PIV04). Base SYSTÈME (schéma
-- ingestion), scopé tenant_id. Valeur BRUTE du système source : jamais interprétée ici (CLAUDE.md
-- n°2 — aucune règle fiscale inventée). Upsert par (tenant_id, code) : occurrences = DERNIÈRE
-- observation (remplacée, non cumulée → un retry ne double-compte pas), libellé et horodatage
-- rafraîchis. Consommé par TVA03 (détection de couverture : régime source non mappé — présence).
CREATE TABLE IF NOT EXISTS ingestion.source_tax_regimes (
    tenant_id    text        NOT NULL,
    code         text        NOT NULL,
    label        text,
    occurrences  bigint      NOT NULL DEFAULT 0,
    last_seen_at timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_source_tax_regimes PRIMARY KEY (tenant_id, code)
);
