-- Schéma conteneur du module DocumentApproval (workflow de validation de document générique, ADR-0028).
-- Tenant-scopé : créé dans CHAQUE base tenant au provisioning (CLAUDE.md n°9). Nom NON collisionnant avec
-- le module Validation existant (règles EN 16931).
CREATE SCHEMA IF NOT EXISTS documentapproval;
