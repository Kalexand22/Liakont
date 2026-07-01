-- Schéma du CATALOGUE GED (F19 §2.1/§3.3, item GED02). Définitions de la config vivante : registre
-- polymorphe des types d'entité, définitions d'axes typés, vocabulaire enum, journal de changement de
-- config (append-only). Vit dans la base DU TENANT (database-per-tenant, blueprint §7) : l'isolation EST
-- la connexion (IConnectionFactory route vers la base du tenant, F19 §3.2) ; aucune colonne tenant_id.
-- GED02 crée le schéma VIDE : AUCUNE table métier ici (les tables entity_types / axis_definitions /
-- axis_values / catalog_change_log arrivent avec GED03a — entity_types AVANT axis_definitions, RL-07).
CREATE SCHEMA IF NOT EXISTS ged_catalog;
