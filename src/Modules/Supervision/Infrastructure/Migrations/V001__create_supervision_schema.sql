-- Schéma du module Supervision (F12 §5, item SUP01a). Vit dans la base DU TENANT (database-per-tenant,
-- blueprint §7) : le dead-man's-switch évalue chaque tenant via TenantJobRunner (SOL06), une exécution
-- par base. Les alertes sont donc persistées dans la base du tenant évalué ; le dashboard d'instance
-- (SUP02) AGRÈGE ces alertes tenant par tenant — seul cas cross-tenant du produit, en LECTURE seule
-- (blueprint §7 règle 2). À la différence de la piste d'audit (DocumentEvent, append-only), une alerte
-- est de l'état OPÉRATIONNEL MUTABLE (auto-résolution + acquittement) — ce n'est pas une table d'audit.
CREATE SCHEMA IF NOT EXISTS supervision;
