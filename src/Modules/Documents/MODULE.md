# Module Documents

## Purpose

Détient le **document métier** de la passerelle et sa **piste d'audit fiable** (F06-Tracking-Piste-Audit).
Un document représente une facture/un avoir détecté(e) côté source, suivi(e) de sa détection jusqu'à son
émission et sa conservation. Le module porte l'état du document, ses montants de contrôle et l'empreinte
du payload pivot, et journalise de façon **immuable** chaque fait d'audit (`DocumentEvent`).

**Périmètre de l'item TRK01** : le **schéma** PostgreSQL (4 tables : `documents`, `document_events`,
`tax_reports`, `archive_entries`), l'agrégat `Document` créé en état `Detected`, la piste d'audit
**append-only** `DocumentEvent`, le **repository** Dapper (création idempotente, upsert par identifiant,
ajout d'événement) et les **lectures** pour l'API/console (par identifiant, par numéro, par état paginé,
piste d'audit), enfin le **câblage du port d'ingestion** `IDocumentIntake` (PIV04) : un push agent crée
un document `Detected` avec son événement de genèse.

Sont **hors périmètre TRK01** (items suivants, qui n'inventent pas de règle ici) : la machine à états et
le supersede (TRK02), l'anti-doublon et la détection d'altération après émission (TRK03), les snapshots
d'émission/rejet de la piste d'audit + le module Payments (TRK04), le coffre WORM (`archive_entries`
alimentée par TRK05) et les tax reports DGFiP (`tax_reports` alimentée par TRK06). Ces deux dernières
tables sont **créées** ici (schéma) mais **alimentées** par leurs items.

## Boundaries

- **Schéma owné** : `documents` (PostgreSQL, base **par tenant**).
  - `documents` : le document métier (numéro, type brut, dates, SIREN, montants NUMERIC, état, empreinte).
  - `document_events` : piste d'audit **append-only** (cœur de la non-altération, F06 §3).
  - `tax_reports` : tax reports DGFiP récupérés (schéma seul ; alimentée par TRK06).
  - `archive_entries` : références des paquets du coffre WORM (schéma seul ; alimentée par TRK05).
- **Isolation tenant** : **par la CONNEXION** — la connexion EST le tenant (database-per-tenant,
  blueprint §7 ; amendement stockage F06 du 2026-06-03). Il n'y a **aucune colonne de tenant** dans ce
  schéma et **aucune requête cross-tenant n'est possible** (CLAUDE.md n°9/17). Les lectures utilisent la
  connexion scopée au tenant courant ; l'ingestion (port d'ingestion) cible le tenant par **slug** (la
  résolution clé API → tenant précède tout contexte tenant, F12 §3.1).
- **Interdits** (module-rules §2) : tout chemin d'update/delete sur `document_events` (append-only),
  toute purge automatique d'une table d'audit, tout type flottant sur un montant, tout calcul/validation
  fiscale (délégués à Validation/TvaMapping), toute classification facture/avoir (Validation).
- **Surface publique** : `Contracts/` uniquement (`IDocumentQueries` + DTOs). L'unité de travail
  d'écriture (`IDocumentUnitOfWork`) est **interne** au module (consommée par le port d'ingestion et,
  à terme, par le pipeline).

## Published Events

Aucun (item TRK01). Le document est créé en réaction à l'ingestion ; il ne publie pas d'événement.

## Consumed Events

Aucun (item TRK01). Le déclencheur DURABLE du pipeline aval est l'événement d'intégration
`ingestion.document.received` publié par l'ingestion (PIV04) **consommé par PIP01** ; la consommation
idempotente de cet événement par le module Documents (filet de rattrapage du port synchrone) est câblée
avec **TRK02** (contrat de cohérence d'`IDocumentIntake`). En TRK01, seul le port synchrone est branché.

## Dependencies

- `Liakont.Agent.Contracts` (`PivotDocumentDto` et ses sous-DTO — porté par `DetectedDocumentIntake`).
- `Liakont.Modules.Ingestion.Contracts` (`IDocumentIntake`, `DetectedDocumentIntake` — port implémenté ;
  accès inter-module par les Contracts uniquement, module-rules §3 / CLAUDE.md n°14).
- `Stratum.Common.Infrastructure` (`IConnectionFactory`, `ITenantConnectionFactory`, `TransactionScope`,
  `MigrationAssembliesOptions`, Dapper/Npgsql/DbUp).
