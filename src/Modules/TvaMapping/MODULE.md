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
(régime non couvert ou flags non satisfaits → `block`, jamais de mapping deviné — INV-007). Le
matching est **EXACT** sur `(code régime source, part)` : pas de joker (le `"*"` pré-v6 de F03 §4.1
est obsolète et rejeté par le validateur — amendement F03 §4.1 du 2026-06-04, INV-011) ; la couverture
des frais s'exprime par une règle explicite par régime (F03 §3, « régime par régime »). Le taux
`ComputedFromSource` est signalé par le moteur ; sa valeur numérique est résolue en aval (pipeline
PIP01) à partir des montants de la ligne (F03 §4.1). Le moteur est testé **en direct** (unitaire) ;
son câblage à l'ingestion (événement `DocumentReceived`) arrive avec PIP01, et son passage sur les
golden files via le seed d'exemple avec TVA04.

**Périmètre de l'item TVA03** : la **détection proactive des régimes non mappés**
(`Domain/CoverageDetection` + requête `GetMappingCoverageReportQuery`, F03 §4.3). `MappingCoverageAnalyzer` (service de domaine PUR
et SANS ÉTAT, comme `TvaMapper`) croise les **régimes source observés** du tenant (métadonnées de push
de l'agent, persistées par tenant en base système — PIV04, lues via le **contrat** du module Ingestion
`ISourceTaxRegimeQueries`) avec les codes couverts par la table de mapping du tenant, et produit un
`MappingCoverageReport` (couverts / absents + occurrences + verdict complet/incomplet). La couverture
est évaluée au **grain du CODE** (un code est couvert dès qu'une règle le référence, toutes parts
confondues) car les métadonnées observées ne portent pas la part ; le contrôle fin par `(code, part)`
reste celui du moteur à l'exécution (TVA02, INV-007). La comparaison est **EXACTE** (`Ordinal`),
cohérente avec le matching du moteur (INV-011/012). Le rapport est **recalculé à la demande** : il est
donc toujours à jour après chaque push d'agent et chaque modification de table (F03 §4.3), sans
projection persistée. Le handler résout la **double clé de tenant** (slug via `ITenantContext` pour les
régimes en base système, `company_id` via `ICompanyFilter` pour la table en base tenant) — jamais de
lecture cross-tenant (CLAUDE.md n°9/17). Consommé par la console (« Complétez la table des régimes de
TVA », WEB07) et exploitable par le pipeline (PIP01).

Le seed d'exemple (TVA04) et l'édition console + journal append-only (TVA05) sont des items distincts.

## Boundaries

- **Schéma owné** : `tvamapping` (PostgreSQL, base **par tenant**).
  - `mapping_tables` : en-tête (version, validateur, date de validation, comportement par défaut).
  - `mapping_rules` : règles (code régime source, part, flags, catégorie, VATEX, mode + valeur de taux).
- **Lit / écrit** : uniquement son propre schéma, toujours scopé par `company_id` (CLAUDE.md n°9).
- **Interdits** (module-rules §2) : règles fiscales en dur, données client embarquées, lecture
  cross-tenant, type flottant sur un taux.
- **Surface publique** : `Contracts/` uniquement (`ITvaMappingQueries`, `GetMappingCoverageReportQuery`,
  DTOs dont `MappingCoverageReportDto`). L'unité de travail d'écriture (`ITvaMappingUnitOfWork`) est
  **interne** au module (consommée par TVA04/TVA05).
- **Accès inter-module** : lecture seule des régimes source observés via le **contrat** du module
  Ingestion (`ISourceTaxRegimeQueries`) — autorisé uniquement par les Contracts (module-rules §3,
  CLAUDE.md n°14) ; aucune référence à `Ingestion.Domain/Application/Infrastructure`.

## Published Events

Aucun. Le moteur (TVA02) PRODUIT une `MappingTrace` par ligne mappée, mais ne la persiste pas
lui-même : elle est attachée à la ligne et persistée par le module Documents (TRK01/04) lors du
câblage du pipeline (PIP01). Le module TvaMapping reste sans événement publié.

## Consumed Events

Aucun. TVA03 lit les régimes source observés **à la demande** (requête, pas par événement) via le
contrat du module Ingestion.

## Dependencies

- `Liakont.Agent.Contracts` (`VatCategory` — code UNCL5305 résultat du mapping, défini une seule fois).
- `Liakont.Modules.Ingestion.Contracts` (`ISourceTaxRegimeQueries`, `SourceTaxRegimeSummaryDto` — régimes
  source observés par tenant, TVA03 ; accès inter-module par les Contracts uniquement).
- `Stratum.Common.Abstractions` (`DomainException`, `ConflictException`, `ITenantContext`).
- `Stratum.Common.Infrastructure` (`IConnectionFactory`, `ICompanyFilter`, `TransactionScope`, `MigrationAssembliesOptions`, Dapper/Npgsql/DbUp).
