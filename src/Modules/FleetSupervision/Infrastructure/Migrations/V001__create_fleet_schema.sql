-- Schéma de la méta-supervision de flotte (OPS04). Vit dans la base SYSTÈME de l'instance CENTRALE
-- (mutualisée IT Innovations) : une « instance » est une ressource de niveau plateforme, AU-DESSUS des
-- tenants (le module Supervision, lui, agit dans la base de CHAQUE tenant). Le store n'accède à cette table
-- que par ISystemConnectionFactory (base système), jamais par une connexion tenant.
-- NOTE mécanique de migration : le runner de migrations applique les scripts d'un module À LA FOIS sur la
-- base système (MigrationRunner) ET sur chaque base tenant (TenantProvisioningService). Le schéma fleet est
-- donc aussi créé, VIDE et inutilisé, dans les bases tenant — aucune donnée tenant n'y est jamais écrite
-- (le store cible exclusivement la base système). C'est sans effet fonctionnel ni fuite cross-tenant.
CREATE SCHEMA IF NOT EXISTS fleet;
