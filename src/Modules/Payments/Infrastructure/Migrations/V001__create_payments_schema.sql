-- Schéma du module Payments (F09 — e-reporting de paiement, item TRK04). Vit dans la base DU TENANT
-- (database-per-tenant, blueprint §7) : l'isolation est ASSURÉE PAR LA CONNEXION (la connexion = le tenant),
-- aucune colonne de tenant dans ce schéma. Le schéma porte les encaissements bruts (payments), les agrégats
-- jour × taux (payment_aggregates) et leur piste d'audit append-only (payment_aggregate_events).
CREATE SCHEMA IF NOT EXISTS payments;
