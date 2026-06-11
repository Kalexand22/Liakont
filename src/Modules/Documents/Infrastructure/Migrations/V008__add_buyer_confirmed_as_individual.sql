-- API02b — Verdict garde-fou B2B/B2C (F08 §A.4).
-- Marqueur PERSISTANT « acheteur confirmé particulier (B2C) » posé par l'opérateur depuis l'endpoint
-- /documents/{id}/verdict. Quand true, la re-vérification (/documents/{id}/recheck) ne re-bloque PAS le
-- document sur le garde-fou BUYER_LOOKS_PROFESSIONAL (VAL05) : la décision tranchée et journalisée prime
-- sur l'heuristique d'indice. Défaut false (cas nominal). Colonne tenant-scopée (base du tenant,
-- database-per-tenant, blueprint §7).
ALTER TABLE documents.documents
    ADD COLUMN buyer_confirmed_as_individual boolean NOT NULL DEFAULT false;
