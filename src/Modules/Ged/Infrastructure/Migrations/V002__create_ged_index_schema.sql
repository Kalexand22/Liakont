-- Schéma d'INDEX GED (F19 §2.1/§3.4, item GED02). Instances & liens : managed_documents (entité-pivot
-- d'index, soft-links SANS FK vers le fiscal), document_axis_links (append-only pur, anti-EAV), le graphe
-- (entity_instances / entity_relations / document_entity_links), la table dérivée de recherche
-- (document_search) et le journal de consultation (consultation_log, append-only). Vit dans la base DU
-- TENANT, comme ged_catalog (isolation par la connexion, F19 §3.2) ; aucune colonne tenant_id.
-- GED02 crée le schéma VIDE : AUCUNE table métier ici (les tables arrivent avec GED03b/GED03c/GED08/GED13).
CREATE SCHEMA IF NOT EXISTS ged_index;
