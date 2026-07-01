# Scénarios de test — module Ged

## Unit (`Liakont.Modules.Ged.Tests.Unit`) — livrés par GED02 (scaffold)

### Frontières NetArchTest — `GedBoundaryTests` (F19 §7/§8, INV-GED-08, module-rules §3, CLAUDE.md n°14)

- `Ged_layers_only_reference_other_modules_through_their_Contracts` — scan DÉCLARATIF (source-tree) des
  `.csproj` du module GED (Contracts/Domain/Application/Infrastructure/Web) : toute `ProjectReference` vers
  un **autre** module `Liakont.Modules.*` doit viser un projet `*.Contracts` (jamais Domain/Application/
  Infrastructure). Couvre la « référence sèche » (ajoutée mais type non encore utilisé). Plancher de
  cohérence : les 5 couches GED sont trouvées (sinon faux-vert à vide).
- `Fiscal_flow_modules_never_reference_Ged` — scan DÉCLARATIF des `.csproj` de **Pipeline / Validation /
  Transmission / Documents** (toutes couches) : **aucune** `ProjectReference` vers `Liakont.Modules.Ged.*`
  (le flux fiscal IGNORE la GED — P1, F19 §7). Plancher : les 4 modules fiscaux sont trouvés.

### Scaffold de migrations — `GedMigrationScaffoldTests` (acceptance GED02, INV-GED-06)

- `The_three_ged_schemas_are_created_by_embedded_migrations` — les 3 scripts `Migrations/*.sql` sont
  **embarqués** dans l'assembly d'Infrastructure (donc découverts par le filtre DbUp `.Migrations.`) et
  chacun crée **son** schéma (`ged_catalog`, `ged_index`, `ged_ingestion`) via `CREATE SCHEMA IF NOT EXISTS`.
- `No_ged_migration_creates_a_business_table_yet` — **aucun** script GED02 ne contient `CREATE TABLE`
  (acceptance « schémas créés VIDES, aucune table métier » ; les tables arrivent aux items GED03+).
- `AddGedModule_registers_the_infrastructure_assembly_for_migrations` — `AddGedModule` déclare l'assembly
  d'Infrastructure dans `MigrationAssembliesOptions` (sinon les schémas ne seraient jamais appliqués).

## Integration (`Liakont.Modules.Ged.Tests.Integration`, PostgreSQL réel) — à venir

Les scénarios base-réelle sont portés par les items qui livrent le comportement correspondant (F19 §8) :

- **GED03a** — ordre FK `axis_definitions → entity_types` (RL-07) ; `value_number` decimal + arrondi half-up ;
  `catalog_change_log` append-only ; check anti-littéral.
- **GED03b** — `document_axis_links` append-only PUR + `current_axis_links` (rétractées/superséedées
  exclues) ; `ck_dal_value_or_retraction` ; anti-EAV (INV-GED-01).
- **GED03c** — graphe append-only + vues `current_*` ; `ck_er_no_self` ; rétractation multi-valeur (RL-24) ;
  `attributes` présentation-only (INV-GED-04).
- **GED04** — mono-valeur **sous concurrence** (INV-GED-03, RL-02 ; deux écritures simultanées ⇒ 1 valeur).
- **GED05b** — ingestion atomique (registre GED + événement) ; idempotence replay ET concurrence (RL-04) ;
  aucune ligne `documents.documents`.
- **GED07** — rangement WORM `_ged/` (option C) ; hash facture inchangé (INV-ARCH-GED-1/2, P1).
- **GED08** — recherche multi-axes correcte ; prédicat de confidentialité MATÉRIALISÉ (RL-31) ; graphe borné
  bidirectionnel (INV-GED-09) ; isolation cross-tenant.
- **GED11** — lints anti-littéral + SQL cross-schéma, chacun avec self-test (RL-27).
- **GED12** — goldens mapping + DEFER (INV-GED-05) ; `GedMappingChangeLog` append-only.
- **GED13** — `consultation_log` append-only en base tenant (INV-GED-11) ; masquage confidentiel en log.

## bUnit / Playwright — pages du portail (GED09a/b/c)

Chaque page `/ged/*` (recherche, document, objet) est livrée avec ses tests bUnit et/ou Playwright
(module-rules §19) ; vue-pure testée, aucune logique métier en page (déléguée aux handlers MediatR).
