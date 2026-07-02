-- Motif de déférement d'un ManagedDocument (F19 §4.5 / INV-GED-05, item GED05b). Colonne ADDITIVE (nullable) sur
-- ged_index.managed_documents (V008, table MUTABLE — l'ajout de colonne est licite, ≠ tables append-only). Quand le
-- consommateur d'ingestion range un document en `deferred` (profil absent/non validé, axe obligatoire non résolu,
-- valeur ambiguë, type d'entité inconnu, contenu stagé altéré…), il pose ICI le motif HUMAIN et ACTIONNABLE (français,
-- n°12) afin qu'il soit VISIBLE EN CONSOLE (§4.5 : « rangé en attente, jamais rejeté en silence ») et exploitable par
-- les pages GED (GED09). NULL pour un document `indexed`/`archived` (aucun déférement). Base DU TENANT (isolation = la
-- connexion, F19 §3.2).
ALTER TABLE ged_index.managed_documents
    ADD COLUMN IF NOT EXISTS defer_reason text;
