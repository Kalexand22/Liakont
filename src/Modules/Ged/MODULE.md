# Module Ged

## Purpose

GED **dynamique** & **coffre-fort documentaire** (F19) : une couche d'indexation métier, de recherche
multidimensionnelle, de graphe et d'ingestion **générique** **AU-DESSUS** du coffre WORM existant, **SANS
toucher au flux fiscal e-invoicing**. La GED classe/retrouve *tout objet métier* (pas seulement des
factures) via un **méta-modèle générique** : des **axes** déclarés (paramétrage tenant) portent les valeurs,
un **graphe d'entités** relie les objets, un **coffre write-once** range les pièces. C'est un **upsell**
découplé du produit e-invoicing (activation = capacité tenant en base, D5).

Le méta-modèle est **générique par construction** (ADR-0032, anti-EAV) : **aucun** vocabulaire métier
(`lot`, `vente`, `pv`, `enchères`, `adjudication`, `acheteur`, `bordereau`) n'est codé dans
`src/Modules/Ged/**` — les axes/entités/relations sont du **paramétrage** (seeds fictifs en
`deployments/<demo>/`, garde outillée GED11). La même table `document_axis_links` porte des euros ET des
pourcentages sans un seul `ALTER TABLE`.

**Périmètre de l'item GED02** (scaffold ; le méta-modèle et les flux arrivent aux items suivants) :

- Le **scaffold** du module (couches Contracts / Domain / Application / Infrastructure / Web, pattern
  Stratum) et ses **livrables documentaires** (`MODULE.md` / `INVARIANTS.md` / `SCENARIOS.md`,
  module-rules §11).
- Les **trois schémas PostgreSQL créés VIDES** par migration DbUp — **aucune table métier** :
  - `ged_catalog` (base **tenant**) : définitions de la config vivante (types d'entité, axes, vocabulaire).
  - `ged_index` (base **tenant**) : instances, liens, graphe, index de recherche, journal de consultation.
  - `ged_ingestion` (base **système**) : registre d'ingestion GED + outbox (co-localisé, écriture atomique).
- Le **wiring DI** (`AddGedModule` → déclaration de l'assembly de migrations) et l'enregistrement au Host.
- Les **gardes de frontière** NetArchTest : le module GED n'accède aux autres modules que par leurs
  `Contracts` ; le **flux fiscal** (Pipeline / Validation / Transmission / Documents) **ne dépend jamais**
  de `Ged.*` (le flux fiscal ignore la GED).

Sont **hors périmètre GED02** (items suivants) : les migrations du méta-modèle (`entity_types`,
`axis_definitions`, `axis_values`, `catalog_change_log` — **GED03a** ; `managed_documents`,
`document_axis_links` + vues + triggers — **GED03b** ; le graphe — **GED03c**) ; l'écriture de valeurs d'axe
sous concurrence (**GED04**) ; le contrat agent GED add-only (**GED05a**) et l'ingestion plateforme
(**GED05b**) ; les permissions GED (**GED06**) ; l'archivage générique WORM `_ged/` (**GED07**) ; la
recherche `tsvector` + facettes + graphe (**GED08**) ; le portail Blazor (**GED09a/b/c**) ; le backfill +
démos (**GED10**) ; les gardes outillées self-testées (**GED11**) ; le mapping déclaratif (**GED12**) ; le
journal de consultation (**GED13**). Fast-follow HORS gate : coffre probant tiers (**GED20**), OpenSearch
(**GED21**), pgvector (**GED22**), OCR (**GED23**), relations avancées (**GED24**).

## Boundaries

- **Schémas ownés** (créés VIDES par GED02, tables ajoutées aux items suivants) :
  - `ged_catalog` (PostgreSQL, base **par tenant**) — définitions (config vivante, MUTABLE ; le
    `catalog_change_log` est append-only).
  - `ged_index` (PostgreSQL, base **par tenant**) — instances, liens (append-only PUR, révision par
    chaînage `supersedes_id`), graphe, index de recherche dérivé (reconstructible), `consultation_log`
    (append-only).
  - `ged_ingestion` (PostgreSQL, base **SYSTÈME**) — registre de réception du canal GED + outbox.
    **Exception documentée** (F19 §3.2 (a)) à « aucune écriture cross-base » : co-localisé avec l'outbox
    pour écrire **atomiquement** le registre + l'événement `ManagedDocumentReceivedV1` (pas de 2PC entre
    deux bases PG), exactement comme `ingestion.received_documents`. Écrit via `ISystemConnectionFactory`.
- **Isolation tenant** : **par la CONNEXION** (database-per-tenant, blueprint §7 ; `IConnectionFactory`
  route vers la base du tenant). `ged_ingestion` (base système) porte un `tenant_id` (slug résolu à
  l'ingestion), lu/écrit toujours **scopé au tenant** de l'agent authentifié — jamais de requête
  cross-tenant (CLAUDE.md n°9). Aucune vue Supervision cross-tenant sur la GED en V1.
- **Frontière avec le flux fiscal** (F19 §7, dure) : le module `Ged` est un **silo isolé**. Le flux
  fiscal (Pipeline / Validation / Transmission / Documents) **NE référence JAMAIS** `Ged.*` (P1) ; le
  module `Ged` relie le fiscal uniquement par **soft-link logique** (colonnes `fiscal_document_id` /
  `archive_entry_id` **sans FK**, projection à la lecture — jamais de copie d'état fiscal, RL-22). La
  **jointure SQL cross-schéma** `ged_* → documents.` / `mandats.` / `tvamapping.` est **interdite** (garde
  lint GED11, invisible à NetArchTest).
- **Surface publique** : `Contracts/` uniquement (module-rules §3). L'événement d'intégration GED vit dans
  `Ged.Contracts` (PAS `Ingestion.Contracts`, RL-17). Domain / Application / Infrastructure sont internes.
- **Interdits** (module-rules §2, F19 §7) : toute **règle fiscale/légale/probante inventée** (la GED n'en
  porte aucune) ; tout **vocabulaire métier en dur** (axes/entités = paramétrage tenant) ; tout
  `if (x is Concret)` (coffre tiers piloté par `SealedArchiveCapabilities`, recherche par
  `IDocumentSearchIndex`) ; tout `double`/`float` sur un axe `number` (decimal, échelle par axe, half-up) ;
  tout chemin d'UPDATE/DELETE sur une table append-only ou sur `documents.archive_entries` (option C, WORM) ;
  toute logique métier côté agent ; tout secret en clair.

## Published Events

Aucun (item GED02, scaffold). L'événement d'intégration `ManagedDocumentReceivedV1`
(`ged.managed-document.received`, dans `Ged.Contracts`) et son `GedEventTypeRegistrar` arrivent avec
**GED05b** (drainé par l'outbox existant).

## Consumed Events

Aucun (item GED02, scaffold). À partir de GED08, l'index de recherche est peuplé par **projection
asynchrone** consommant `ManagedDocumentReceivedV1` (+ événements de mapping/archivage GED) — **JAMAIS**
l'événement fiscal `DocumentReceivedV1` (F19 §6.1). Le pont « une facture apparaît aussi en GED » est un
consommateur dédié (GED10 backfill / consommateur fiscal→GED), jamais un abonnement du module `Ged` au flux
d'émission.

## Dependencies

- `Stratum.Common.Abstractions` — abstractions socle (multi-tenancy, connexion).
- `Stratum.Common.Infrastructure` — `MigrationAssembliesOptions` (runner DbUp), `IConnectionFactory` /
  `ISystemConnectionFactory`, Dapper / Npgsql / DbUp (arrivent avec les items de persistance).
- `Microsoft.AspNetCore.App` (FrameworkReference) — DI / Options / Logging du framework partagé (aucun
  package NuGet nouveau, aucun ADR).

À ce stade (scaffold), le module ne référence **aucun** autre module métier. Les dépendances inter-modules
ultérieures se font **uniquement par leurs `Contracts`** (module-rules §3) : `Archive.Contracts`
(archivage générique WORM, GED07), `Documents.Contracts` (projection d'état fiscal à la lecture, GED09b),
`Staging.Contracts` (relecture du pivot GED, GED05b), et côté agent `Liakont.Agent.Core` /
`Liakont.Agent.Contracts.Ged` (contrat GED add-only, GED05a).
