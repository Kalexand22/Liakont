# Module TvaMapping

## Purpose

Cœur de valeur fiscale du produit (et zone de risque n°1) : transformer un **code régime TVA du
système source** en triplet normalisé **{catégorie EN 16931 (UNCL5305), taux, code VATEX}** via une
**table de mapping paramétrée par tenant** et **validée humainement** par l'expert-comptable
(F03-Mapping-TVA.md).

**Périmètre de l'item TVA01** : le **modèle** de la table (`MappingTable` + `MappingRule`), sa
**persistance** PostgreSQL par tenant, et sa **validation structurelle** à l'écriture comme au
chargement.

**Périmètre de l'item TVA02** : le **moteur** `TvaMapper` (`Domain/Services`) + ses types
(`Domain/Mapping` : `MappingRequest`, `MappingResult`, `MappingTrace`). Service de domaine PUR et
SANS ÉTAT : il applique la table du tenant à `(code régime source, part, flags)` et produit soit le
triplet `{catégorie UNCL5305, taux, VATEX}` avec une `MappingTrace` d'audit, soit un blocage
(régime non couvert ou flags non satisfaits → `block`, jamais de mapping deviné — INV-007). Le taux
`ComputedFromSource` est signalé par le moteur ; sa valeur numérique est résolue en aval (pipeline
PIP01) à partir des montants de la ligne (F03 §4.1). Le moteur est testé **en direct** (unitaire) ;
son câblage à l'ingestion (événement `DocumentReceived`) arrive avec PIP01, et son passage sur les
golden files via le seed d'exemple avec TVA04.

La détection des régimes non mappés (TVA03), le seed d'exemple (TVA04) et l'édition console +
journal append-only (TVA05) sont des items distincts.

## Boundaries

- **Schéma owné** : `tvamapping` (PostgreSQL, base **par tenant**).
  - `mapping_tables` : en-tête (version, validateur, date de validation, comportement par défaut).
  - `mapping_rules` : règles (code régime source, part, flags, catégorie, VATEX, mode + valeur de taux).
- **Lit / écrit** : uniquement son propre schéma, toujours scopé par `company_id` (CLAUDE.md n°9).
- **Interdits** (module-rules §2) : règles fiscales en dur, données client embarquées, lecture
  cross-tenant, type flottant sur un taux.
- **Surface publique** : `Contracts/` uniquement (`ITvaMappingQueries`, DTOs). L'unité de travail
  d'écriture (`ITvaMappingUnitOfWork`) est **interne** au module (consommée par TVA04/TVA05).

## Published Events

Aucun. Le moteur (TVA02) PRODUIT une `MappingTrace` par ligne mappée, mais ne la persiste pas
lui-même : elle est attachée à la ligne et persistée par le module Documents (TRK01/04) lors du
câblage du pipeline (PIP01). Le module TvaMapping reste sans événement publié.

## Consumed Events

Aucun (TVA01).

## Dependencies

- `Liakont.Agent.Contracts` (`VatCategory` — code UNCL5305 résultat du mapping, défini une seule fois).
- `Stratum.Common.Abstractions` (`DomainException`, `ConflictException`).
- `Stratum.Common.Infrastructure` (`IConnectionFactory`, `TransactionScope`, `MigrationAssembliesOptions`, Dapper/Npgsql/DbUp).
