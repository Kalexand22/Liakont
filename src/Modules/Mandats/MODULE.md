# Module Mandats

## Purpose

Socle de l'**autofacturation sous mandat** (type 389, art. 289 I-2 CGI — F15, ADR-0022). Le module
porte le **mandant** (le vendeur au nom et pour le compte duquel le tenant émet) comme **tiers récurrent
de première classe** du tenant — **jamais un sous-tenant** (blueprint §7) — et le **cycle de vie du
mandat** (clause, caractère écrit/tacite, statut d'assujettissement déclaré, délai de contestation,
validation humaine, révocation).

**Périmètre de l'item MND01 (FONDATION — aucune décision fiscale, structure seulement)** :
- le **modèle** : agrégats `Mandant` (clé `(company_id, reference)`) et `Mandat`
  (clé `(company_id, mandant_id, reference)`, gabarit FORT `MappingTable`) ;
- la **persistance** PostgreSQL par tenant (schéma `mandats`, migrations DbUp) ;
- le **journal append-only** `mandat_change_log` (registre + cycle de vie), immuable par double trigger base ;
- le **suspendu-par-défaut** : `AssujettissementStatus`/`ContestationDelay` `null`, mandat non validé ou
  révoqué ⇒ **389 suspendu** (`Mandat.IsSelfBillingSuspended`), jamais un défaut inventé.

**Hors périmètre de MND01 (items suivants du lot, F15/ADR-filles)** :
- `MandatSequence` + numérotation BT-1 par mandant → **MND05** (ADR-0025) ;
- `SelfBilledAcceptance` + machine d'acceptation + `self_billed_acceptance_log` → **MND02** (ADR-0024) ;
- port `ISelfBilledGate` + branchement pipeline → **MND03** ; bascule tacite (`TenantJobRunner`) → **MND04** ;
- re-clé anti-doublon F06 → **MND06** ; projection BT-3=389 vers la PA → **MND07** ;
- les écrans console (registre, édition) — le squelette `Web/MandatsEndpointMapping.cs` est posé, sans route.

Aucune **décision fiscale** n'est prise ici : statuts d'assujettissement admis, valeur du délai, avoir 261
restent **NON TRANCHÉS** (F15 §6) — un item qui les rencontre **bloque**, il ne devine pas.

## Boundaries

- **Schéma owné** : `mandats` (PostgreSQL, base **par tenant**, database-per-tenant ADR-0011).
  - `mandants` : registre des mandants (référence, raison sociale, n° TVA BT-31 nullable, SIREN, préfixe).
  - `mandats` : mandats (clause, écrit/tacite, statut, délai, validation, révocation).
  - `mandat_change_log` : journal **append-only** (registre + cycle de vie), immuable par double trigger.
- **Lit / écrit** : uniquement son propre schéma, toujours scopé par `company_id` (CLAUDE.md n°9, INV-MANDATS-1).
- **Interdits** (module-rules §2) : montant sur le mandat, règle fiscale inventée, donnée client embarquée,
  lecture cross-tenant, mandant traité en sous-tenant, chemin d'update/delete sur le journal.
- **Surface publique** : `Contracts/` uniquement (`IMandatsQueries`, DTOs `MandantDto`/`MandatDto`/
  `MandatChangeLogEntryDto`). L'unité de travail d'écriture (`IMandatsUnitOfWork`) est **interne** au module.
- **Web** : `Web/MandatsEndpointMapping.cs` — squelette de montage (aucune route en MND01).

## Published Events

Aucun.

## Consumed Events

Aucun.

## Dependencies

- `Stratum.Common.Abstractions` (`ConflictException`).
- `Stratum.Common.Infrastructure` (`IConnectionFactory`, `TransactionScope`, `MigrationAssembliesOptions`,
  Dapper/Npgsql/DbUp).
- Aucune dépendance sur un autre module métier (frontière NetArchTest, INV-MANDATS-2).
