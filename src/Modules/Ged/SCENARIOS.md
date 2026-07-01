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

### Mapping déclaratif générique — GED12 (F19 §4.5, INV-GED-05)

- `GedSelectorTests` (`Domain.Mapping.GedSelector`) — sélecteur **JSONPath restreint** (chemins simples +
  filtre d'égalité + joker) sur un `IngestedDocumentDto` BRUT : `$.fields.x`, `$.axes[?name=='a'].values[*]`,
  `$.entities[?type=='t'].externalId` (filtre), valeurs nulles/absentes ignorées ; toute **syntaxe invalide**
  est refusée à la validation (jamais deviner l'intention, règle 3).
- `GedMapperTests` (`Domain.Mapping.GedMapper`) — **goldens** brut → axes/entités/relations d'un profil VALIDÉ ;
  interprétation d'un axe `number` en **decimal half-up** (n°1) ; **cas DEFER** (INV-GED-05) : profil absent /
  non validé, axe **obligatoire** non résolu, axe **mono-valeur** ambigu, valeur source **incompatible** avec le
  `data_type`, axe inconnu du catalogue — jamais deviner ni inventer (règles 2/3). Vocabulaire NEUTRE (généricité).
- `GedMappingProfileTests` (`Domain.Mapping.GedMappingProfile`) — validation structurelle (type/version vide,
  code d'axe **dupliqué**, sélecteur mal formé, validation incohérente) ; sémantique **validé / non validé**
  (miroir `MappingTable` : jamais appliqué non validé ; `Invalidate` retombe non validé).

### Ingestion générique — GED05b (F19 §4.3, INV-GED-06, RL-01)

- `GedIngestionDecisionTests` (`Domain.Ingestion.GedIngestionDecision`) — **golden 3 cas** de l'anti-doublon GED
  (`AcceptedNew` / `AcceptedAltered` / `Duplicate`) et l'**ordre d'évaluation** (doublon strict testé AVANT
  l'altération : un renvoi du même contenu n'est jamais une fausse altération). Logique RE-COPIÉE du canal fiscal
  (RL-01), testée indépendamment.
- `GedCanonicalJsonReaderTests` (`Infrastructure.Serialization.GedCanonicalJsonReader`) — **round-trip** octet par
  octet `GedCanonicalJson.Serialize(Read(json)) == json` (document riche ET minimal : optionnels omis, `SourceFields`
  objet vide) ; le lecteur reconstruit fidèlement champs/axes/entités/relations (libellé optionnel absent → null) pour
  que le mapping aval voie un pivot exact.

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
- **GED05b — LIVRÉ** (`ManagedDocumentIngestionIntegrationTests`, collection `GedIntegration`, base isolée par
  test) : (1) un document non-facture accepté écrit ATOMIQUEMENT le registre GED (`ged_ingestion.ged_received_documents`,
  base système) ET l'événement `ManagedDocumentReceivedV1` (outbox), la ré-ingestion du même contenu étant un
  **Duplicate** idempotent (aucune 2ᵉ ligne, aucun 2ᵉ événement — anti-doublon `(tenant, hash)`, INV-GED-06) ; (2) le
  consommateur relit le pivot stagé, mappe (**GedMapper**) et écrit `managed_documents` (`status='indexed'`) + le lien
  d'axe, un **replay** étant un NO-OP (`ON CONFLICT (id) DO NOTHING` + garde de statut, RL-04) ; (3) **deux livraisons
  SIMULTANÉES** de l'événement écrivent les liens **UNE SEULE FOIS** (verrou consultatif par document + garde de statut,
  RL-04 — un test séquentiel serait un faux-vert) ; (4) un document **sans profil validé** est rangé `deferred` avec un
  motif français actionnable (`defer_reason`, INV-GED-05) et n'écrit aucun lien ; (5) l'indexation est **tenant-scopée**
  par la connexion (≥ 2 bases : le tenant B reste vide). AUCUN Document/état fiscal n'est atteint (le canal GED n'appelle
  jamais `IDocumentIntake`, l'événement est disjoint de `DocumentReceivedV1`).
- **GED07** — rangement WORM `_ged/` (option C) ; hash facture inchangé (INV-ARCH-GED-1/2, P1).
- **GED08 — LIVRÉ** (`DocumentSearchIndexIntegrationTests`, collection `GedIntegration`, base isolée par test) :
  projection reconstructible du `search_vector` (DELETE + rebuild ; idempotence UPSERT ; projection asynchrone via
  `ManagedDocumentSearchProjector`) ; plein-texte accent-insensible (wrapper unaccent IMMUTABLE, RL-13) ; recherche
  multi-axes correcte (un axe multi-valeur ne crée pas de faux positif — CASE code+valeur, jamais un count(DISTINCT
  code) naïf) ; prédicat de confidentialité MATÉRIALISÉ dans le SQL sur AXE, FACETTE et GRAPHE, plein-texte excluant
  les axes confidentiels au build (RL-31 / INV-GED-10, anti-oracle) ; graphe borné bidirectionnel — borne de
  profondeur, anti-cycle, keyset, racine et voisins confidentiels exclus sans le droit (INV-GED-09) ; pagination
  keyset (RL-20) ; isolation cross-tenant (≥ 2 bases).
- **GED11** — lints anti-littéral + SQL cross-schéma, chacun avec self-test (RL-27).
- **GED12 — LIVRÉ** (`GedMappingProfileMigrationsIntegrationTests`, collection `GedIntegration`, base isolée
  par test) : un profil **VALIDÉ** fait un round-trip (règles axe/entité/relation préservées via jsonb) ; un
  profil **non validé** n'est JAMAIS rendu applicable (`GetValidatedProfileAsync` → null) ; deux profils validés
  du même `documentType` sont en **conflit** (index unique partiel) ; `ged_mapping_change_log` **append-only**
  (UPDATE / DELETE / TRUNCATE rejetés par trigger, INV-GED-02). Les goldens de mapping purs sont côté Domain
  (`GedMapperTests`, `GedSelectorTests`).
- **GED13** — `consultation_log` append-only en base tenant (INV-GED-11) ; masquage confidentiel en log.

## bUnit / Playwright — pages du portail (GED09a/b/c)

Chaque page `/ged/*` (recherche, document, objet) est livrée avec ses tests bUnit et/ou Playwright
(module-rules §19) ; vue-pure testée, aucune logique métier en page (déléguée aux handlers MediatR).
