# Scénarios de test — module Ged

## Unit (`Liakont.Modules.Ged.Tests.Unit`) — livrés par GED02 (scaffold)

### Frontières — `GedBoundaryTests` (F19 §7/§8, INV-GED-08, module-rules §3, CLAUDE.md n°14)

Deux niveaux complémentaires : **NetArchTest (IL, usage effectif des types)** + **scan déclaratif des
`.csproj` (référence sèche, modèle `StratumPackagingBoundaryTests`)**.

- `Ged_production_assemblies_carry_no_fiscal_dependency` — **NetArchTest (IL)** : aucun type des assemblies
  de production GED (Infrastructure/Domain/Application/Contracts) ne dépend d'un namespace du flux fiscal
  (`Pipeline`/`Validation`/`Transmission`/`Documents`) — « Ged.Domain → aucune dépendance fiscale » (F19 §7).
  Non vacuous : `Ged.Infrastructure` porte `GedModuleRegistration` (types + dépendances réels).
- `Ged_layers_only_reference_other_modules_through_their_Contracts` — **scan DÉCLARATIF** (source-tree) des
  `.csproj` du module GED (Contracts/Domain/Application/Infrastructure/Web) : toute `ProjectReference` vers
  un **autre** module `Liakont.Modules.*` doit viser un projet `*.Contracts` (jamais Domain/Application/
  Infrastructure). Couvre la « référence sèche » (ajoutée mais type non encore utilisé). Plancher de
  cohérence : les 5 couches GED sont trouvées (sinon faux-vert à vide).
- `Fiscal_flow_modules_never_reference_Ged` — **scan DÉCLARATIF** des `.csproj` de **Pipeline / Validation /
  Transmission / Documents** (liste EXPLICITE F19 §7, toutes couches) : **aucune** `ProjectReference` vers
  `Liakont.Modules.Ged.*` (le flux fiscal IGNORE la GED — P1, F19 §7). Plancher : les 4 modules fiscaux sont
  trouvés. La liste est volontairement restreinte à ces 4 modules (un module d'intake référençant la surface
  `Ged.Contracts` est permis par design, module-rules §3, et n'est donc pas une violation).

### Scaffold de migrations — `GedMigrationScaffoldTests` (INV-GED-06 ; anti-littéral INV-GED-12 depuis GED03a)

- `Each_ged_schema_is_created_by_exactly_one_embedded_migration` — les scripts `Migrations/*.sql` sont
  **embarqués** dans l'assembly d'Infrastructure (donc découverts par le filtre DbUp `.Migrations.`) et
  chaque schéma (`ged_catalog`, `ged_index`, `ged_ingestion`) est créé par **exactement un** `CREATE SCHEMA`
  (les tables du méta-modèle vivent dans des migrations séparées, GED03a+).
- `Ged_migrations_hardcode_no_business_vocabulary` — **anti-littéral (GED03a, INV-GED-12 / règle 7)** :
  aucune migration GED ne contient de vocabulaire métier en dur ; les axes / types d'entité sont du
  paramétrage tenant. La garde OUTILLÉE complète (tout `src/Modules/Ged/**` + `ci.yml`) arrive avec GED11.
- `AddGedModule_registers_the_infrastructure_assembly_for_migrations` — `AddGedModule` déclare l'assembly
  d'Infrastructure dans `MigrationAssembliesOptions` (sinon les schémas ne seraient jamais appliqués).

### Catalogue polymorphe & normaliseur — GED03a (F19 §3.3/§3.7)

- `ValueNormalizerTests` (`Domain.Catalog.ValueNormalizer`) — un axe `number` est un **`decimal`, jamais
  double/float** ; arrondi **commercial half-up** à l'échelle de l'axe (`value_scale`, y compris négatifs et
  échelle 0) ; **refus, jamais deviner** pour chaque `data_type` (number/date/boolean/entity/json invalides,
  séparateur de milliers, exposant, valeur vide → `AxisValueFormatException`).
- `CatalogModelTests` (`Domain.Catalog.EntityType` / `AxisDataTypes`) — le type d'entité est **polymorphe**
  (code métier libre, jamais un enum figé) et refuse code/libellé vide ; le système `AxisDataType` fait un
  aller-retour exact avec son code SQL et **refuse tout code hors du vocabulaire technique fermé** (miroir de
  `ck_axis_def_data_type`).

## Integration (`Liakont.Modules.Ged.Tests.Integration`, PostgreSQL réel via Testcontainers)

Les scénarios base-réelle sont portés par les items qui livrent le comportement correspondant (F19 §8) :

- **GED03a — LIVRÉ** (`GedCatalogMigrationsIntegrationTests`, collection `GedIntegration`, base isolée par
  test) : les migrations `ged_catalog` s'appliquent sur base **VIERGE** avec la FK
  `axis_definitions → entity_types` satisfaite (**ordre RL-07** matérialisé par un INSERT d'axe `entity`) ;
  FK `target_entity_type_id` opposable (référence pendante rejetée) ; CHECK `data_type` hors vocabulaire /
  `entity` sans cible / `value_scale` hors [0..9] rejetés ; `catalog_change_log` **append-only** (UPDATE /
  DELETE / TRUNCATE rejetés par trigger). L'arrondi half-up decimal est couvert côté Domain
  (`ValueNormalizerTests`), l'anti-littéral côté scan de migrations (`GedMigrationScaffoldTests`).
- **GED03b — LIVRÉ** (`GedIndexMigrationsIntegrationTests`, collection `GedIntegration`, base isolée par
  test) : `document_axis_links` **append-only PUR** (UPDATE / DELETE / TRUNCATE rejetés par trigger,
  INV-GED-02) ; `ck_dal_value_or_retraction` (lien normal = exactement 1 valeur typée — 0 ou 2 rejetées ;
  rétractation = 0 valeur + `supersedes_id` obligatoire, valeur portée rejetée) ; vue `current_axis_links`
  qui **exclut** une ligne superséedée (révision **par chaînage** `supersedes_id`, jamais d'UPDATE) ET une
  rétractation ET sa cible (RL-24) ; **anti-EAV (INV-GED-01)** structurel : colonnes de valeur TYPÉES,
  **aucune** colonne fourre-tout (`value`/`value_text`), `value_number` = `numeric` (decimal exact, round-trip
  testé — jamais double), `managed_documents` **sans** colonne `search_vector` (foyer FTS unique = GED08) ;
  `managed_document_change_log` **append-only** (UPDATE / DELETE / TRUNCATE rejetés).
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
