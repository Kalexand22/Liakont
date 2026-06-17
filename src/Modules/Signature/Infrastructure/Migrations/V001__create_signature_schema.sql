-- Schéma conteneur du module Signature (volet SUR PLACE — capteur Wacom, ADR-0030). Tenant-scopé : créé
-- dans CHAQUE base tenant au provisioning (CLAUDE.md n°9). SIG03 (abstraction à capacités) n'avait aucune
-- table ; SIG08 introduit la persistance du proxy OnSiteCapture (liaisons de signataire vérifié + journal
-- de preuve append-only). Nom non collisionnant.
CREATE SCHEMA IF NOT EXISTS signature;
