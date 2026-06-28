-- V007 — identifiant de LOT D'ÉMISSION du journal e-reporting B2C de la marge (B4). Une valeur par TENTATIVE
-- d'émission (par POST), partagée par TOUTES les entrées de cet agrégat : les Pending écrites avant le POST ET
-- les issues écrites après. C'est l'identité d'une TRANSMISSION RÉELLE.
--
-- Pourquoi : l'empreinte de contenu (content_hash) n'est PAS unique par transmission. Un document tardif sur un
-- jour DÉJÀ émis part dans un NOUVEL agrégat (POST séparé, pa_emission_id distinct — design B4/D3). Deux
-- transmissions distinctes d'un MÊME contenu (même jour×devise×taux + montants identiques — barème d'honoraires
-- d'enchères standard, fréquent) partagent alors le même content_hash. Regrouper la vue console par content_hash
-- FUSIONNERAIT ces deux transmissions en une ligne (compte de pièces gonflé, un seul id PA visible) : une
-- sous-représentation sur une surface d'AUDIT de transmissions fiscales. Le lot d'émission distingue chaque POST.
--
-- DEFAULT gen_random_uuid() : couvre d'éventuelles lignes antérieures (chacune devient son propre lot — jamais de
-- fusion erronée). Le job écrit une valeur EXPLICITE, partagée par toutes les entrées d'un même POST.
--
-- DDL pur (ADD COLUMN) : ne déclenche pas le trigger append-only (BEFORE UPDATE/DELETE/TRUNCATE par ligne) — la
-- piste d'audit reste immuable (CLAUDE.md n°4).
ALTER TABLE pipeline.b2c_margin_emissions
    ADD COLUMN IF NOT EXISTS emission_batch_id uuid NOT NULL DEFAULT gen_random_uuid();

-- Lecture console : une ligne par lot d'émission (regroupement + dernière entrée par lot).
CREATE INDEX IF NOT EXISTS ix_b2c_margin_emissions_batch
    ON pipeline.b2c_margin_emissions (emission_batch_id);
