# Module TvaMapping

## Purpose

Cœur de valeur fiscale du produit (et zone de risque n°1) : transformer un **code régime TVA du
système source** en triplet normalisé **{catégorie EN 16931 (UNCL5305), taux, code VATEX}** via une
**table de mapping paramétrée par tenant** et **validée humainement** par l'expert-comptable
(F03-Mapping-TVA.md).

**Périmètre de l'item TVA01** : le **modèle** de la table (`MappingTable` + `MappingRule`), sa
**persistance** PostgreSQL par tenant, et sa **validation structurelle** à l'écriture comme au
chargement. Le moteur d'application (TVA02), la détection des régimes non mappés (TVA03), le seed
d'exemple (TVA04) et l'édition console + journal append-only (TVA05) sont des items distincts.

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

Aucun (TVA01). Le moteur (TVA02) attachera une `MappingTrace` aux lignes mappées via le module
Documents ; ce n'est pas le périmètre de TVA01.

## Consumed Events

Aucun (TVA01).

## Dependencies

- `Liakont.Agent.Contracts` (`VatCategory` — code UNCL5305 résultat du mapping, défini une seule fois).
- `Stratum.Common.Abstractions` (`DomainException`, `ConflictException`).
- `Stratum.Common.Infrastructure` (`IConnectionFactory`, `TransactionScope`, `MigrationAssembliesOptions`, Dapper/Npgsql/DbUp).
