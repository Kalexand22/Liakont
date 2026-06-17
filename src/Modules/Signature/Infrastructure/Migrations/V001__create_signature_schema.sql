-- Schéma du module Signature (ADR-0027/0029, SIG07). Porte la persistance du plug-in de signature à distance :
-- comptes de signature chiffrés par tenant, inbox durable de webhooks, liaison demande→document, et le
-- catalogue SYSTÈME de routage par handle opaque. Tenant-scopé par company_id sauf le catalogue de routes
-- (système, interrogé hors scope tenant — ADR-0029 §2). Aucune donnée client en dur (CLAUDE.md n°7).
CREATE SCHEMA IF NOT EXISTS signature;
