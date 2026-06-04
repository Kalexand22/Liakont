# TenantSettings Module

> Premier module métier Liakont (namespace `Liakont.Modules.*`). Spec : `docs/conception/F12-A-Parametrage-Tenant.md` (item CFG01). Implémente CFG02.

## Purpose

Gère le **niveau « Tenant » de configuration** (F12-A) : profil légal du tenant, paramétrage
fiscal, comptes Plateforme Agréée (avec clés API chiffrées), planification d'extraction et seuils
d'alerte. Fournit aussi l'**import de seed** `deployments/<client>/` (provisioning OPS03).

Le module ne porte **aucune logique fiscale** : il stocke des paramètres dont la décision
appartient à l'expert-comptable du client. Les valeurs `null` (paramétrage fiscal) sont
signifiantes — voir INVARIANTS.

## Boundaries

- **Owns:** schéma `tenantsettings` (tables `tenant_profiles`, `fiscal_settings`, `pa_accounts`,
  `extraction_schedules`, `alert_thresholds`). Tables vivant dans la base **par tenant**
  (database-per-tenant, socle Stratum), scopées `company_id`.
- **Reads:** son propre schéma uniquement.
- **Writes:** son propre schéma. Journalise ses mutations via `IActivityLogger` (module Audit,
  append-only) — il n'écrit pas lui-même la table d'audit.
- **Does NOT:** aucune logique fiscale (catégorie/seuil/cadence inventés) ; aucun chemin
  d'update/delete sur une table d'audit ; ne référence aucun plug-in PA concret (le `pluginType`
  est une donnée, pas un `if (pa is …)`) ; n'expose jamais une clé API (ni claire ni chiffrée).

## Configuration et secrets

- **Permission** : toutes les opérations relèvent de `liakont.settings` (`LiakontPermissions.Settings`),
  appliquée à la couche console/endpoint (lot WEB) — F12-A §7. La consultation seule = `liakont.read`.
- **Chiffrement** : les clés API des PA sont chiffrées via **ASP.NET Core Data Protection**
  (`ISecretProtector`). La **persistance des clés DP** (par instance/appliance) et le nom
  d'application stable sont configurés par **OPS01** (hors de ce module). Aucun secret n'est
  jamais écrit en clair en base, dans les logs ou dans une réponse (CLAUDE.md n°10).

## Published Events

Aucun. (Les consommateurs — PIP03, SUP01, WEB01, AGT03 — lisent le paramétrage via les requêtes
MediatR exposées dans `Contracts`.)

## Consumed Events

Aucun.

## Dependencies

- `Common.Abstractions` (MediatR, Audit `IActivityLogger`, Security `IActorContext`).
- `Common.Infrastructure` (Dapper, migrations DbUp, `IConnectionFactory`, `ICompanyFilter`).
- `Microsoft.AspNetCore.App` (framework partagé) pour Data Protection — aucun package NuGet ajouté.
- Aucune dépendance vers un autre module (frontière Contracts-only respectée).
