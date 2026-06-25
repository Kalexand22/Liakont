# ADR-0032 — Méta-modèle GED dynamique : axes typés et entités polymorphes append-only (anti-EAV), module unique `Liakont.Modules.Ged` à trois schémas PostgreSQL

- **Statut** : Proposé (2026-06-25).
- **Date** : 2026-06-25
- **Nature** : cet ADR **précède** le chantier d'implémentation (module `Liakont.Modules.Ged` non démarré,
  **aucun code**). Les sections **Décision** et **Invariants** sont **normatives** : elles décrivent la **cible**,
  pas l'état du code. Aucun invariant n'est garanti tant qu'il n'est pas livré **et** prouvé par test. Cet ADR
  **dérive de** la conception `docs/conception/F19-GED-Dynamique-Coffre-Fort.md` (statut « proposition NON
  RATIFIÉE ») et n'invente **aucune** règle fiscale, légale ou probante (CLAUDE.md n°2). Il est le **socle** des
  quatre ADR GED sœurs : il pose le méta-modèle d'indexation que l'**ingestion** (ADR-0034) **peuple**, que la
  **recherche et le graphe** (ADR-0035) **lisent** via les vues `current_*`, dont l'**archivage probant tiers**
  (ADR-0033) référence le rangement WORM, et dont les consultations sont tracées par le **journal** (ADR-0036).

- **Numérotation** : ADR-**0032**. La numérotation libre de la GED (F19 §9) commence à **0032** (le repo contient
  déjà DEUX `ADR-0031` — `-cablage-cycle-run-agent…` et `-licence-fluentassertions…`). Plan d'ADR GED : **0032**
  méta-modèle, **0033** coffre tiers/option C (fast-follow GED20), **0034** ingestion générique, **0035** recherche
  & index, **0036** journal de consultation. Titres canoniques exacts des sœurs :
  - ADR-0033 — Coffre probant tiers / SAE comme 5ᵉ axe enfichable (`ISealedArchiveProvider`) et archivage WORM des
    documents GED hors chaîne fiscale (option C ; fast-follow GED20)
  - ADR-0034 — Canal d'ingestion générique GED par agents : `IngestedDocumentDto` / `ManagedDocumentReceivedV1`
    add-only, registre dédié en base système, `IManagedExtractor` distinct
  - ADR-0035 — Recherche & index GED : `tsvector` PostgreSQL derrière `IDocumentSearchIndex`, projection asynchrone
    reconstructible, graphe borné bidirectionnel
  - ADR-0036 — Journal de consultation GED append-only (`ged_index.consultation_log`, base tenant, WORM) :
    best-effort par défaut, fail-closed si finalité probante

- **Contexte décisionnel** : `docs/conception/F19-GED-Dynamique-Coffre-Fort.md` §2.1 (décision modulaire un module /
  trois schémas), §2.3 (axes de plug-in), §3 (méta-modèle complet : §3.1 entité documentaire neuve, §3.2 db-per-tenant
  et exceptions, §3.3 `ged_catalog`, §3.4 `ged_index` instances & liens, §3.5 « pas un EAV », §3.6 triggers
  append-only, §3.7 UoW, §3.8 référence cross-schéma), §7 (frontières & règles non négociables), §8 (tests & DoD ;
  jeu d'invariants `INV-GED-01..12`, `INV-ARCH-GED-1..3`), §11 D6/D7 ; sources socle/code réelles citées par F19 :
  `src/Modules/Documents/Migrations/V005__create_archive_entries_table.sql` (triggers WORM
  `reject_archive_entry_mutation` + no-truncate — patron append-only), `src/Modules/Reconciliation/Migrations/V002`
  (soft-link `proposed_document_id` **sans FK**), `src/Modules/TvaMapping/**` (UoW + trigger, `MappingChangeLog`),
  `src/Modules/Ingestion/Migrations/V004__create_received_documents_table.sql` (registre en base système),
  `blueprint.md` §2/§6/§7, `docs/architecture/module-rules.md` ; ADR liés :
  `docs/adr/socle/ADR-0011-database-per-tenant.md` (database-per-tenant), `docs/adr/ADR-0022-mandant-tiers-premiere-classe-module-mandats.md`
  (`Mandats.Mandant`/`Mandat` fiscal), `docs/adr/ADR-0007-serialisation-canonique-pivot.md` (sérialisation canonique).

## Contexte

Karl demande une **GED dynamique** : une couche d'**indexation métier multidimensionnelle**, de **recherche** et
de **graphe de relations**, posée **AU-DESSUS** du coffre probant déjà en production (module `Archive`, WORM), sans
toucher au flux fiscal e-invoicing. L'enjeu de CET ADR est le **méta-modèle de données** : comment indexer des
documents arbitraires (PV, contrats, courriers, bordereaux) par des **axes déclarés par tenant**, et les relier à des
**entités polymorphes** (acheteur, chantier, mandant…), **sans figer un schéma métier dans le produit** et **sans
inventer une règle fiscale ou probante** (F19 §1, CLAUDE.md n°2).

Le **piège central** est l'**EAV** (`document_metadata(key, value)`). Un fourre-tout `(clé, valeur text)` est
séduisant pour la flexibilité, mais il détruit le typage (un montant devient une chaîne, donc un `double` à la
relecture — violation CLAUDE.md n°1), la facette, le tri, la provenance et l'auditabilité. F19 §3.5 le rejette
explicitement. La décision doit donc poser un **vrai méta-modèle typé** : la **clé est une définition d'axe déclarée
et typée** (`axis_definitions`), la **valeur est rangée dans une colonne du bon type** (`value_number numeric`,
`value_date date`…), chaque lien **porte sa provenance**, les **entités sont des objets de première classe**, et tout
est **append-only auditable**.

Une seconde force en présence est la **frontière** : ce registre polymorphe (`entity_types` / `entity_instances`)
ÉVITE de répéter « une table dédiée par type d'entité GED », mais il **ne doit ni remplacer ni absorber** les entités
fiscales existantes — `Mandats.Mandant`/`Mandat` (module fiscal, ADR-0022) et `Stratum.Modules.Party` (socle
**vendoré**, règle review n°20) — sous peine de violer la frontière inter-modules (règle review n°14) et de
modifier silencieusement le socle. Le couplage se fait par **soft-link** (id externe), jamais par ré-hébergement
(F19 §3.3.2).

Un troisième piège, propre à l'append-only **pur**, est la **garde de mono-valeur sous concurrence** : « une seule
valeur courante par (document, axe) » ne peut **pas** reposer sur un index partiel, puisque la notion de « courante »
est un **calcul de chaîne** (`supersedes_id`), pas une colonne. Sous READ COMMITTED (défaut du projet), deux
écritures concurrentes superséderaient chacune la même courante et produiraient une **double valeur courante
permanente et non réparable** (les triggers append-only interdisent tout UPDATE/DELETE). La décision doit donc
poser une garde de concurrence explicite (advisory lock / `FOR UPDATE`), prouvée par un **test concurrent** — un test
séquentiel serait un faux-vert (F19 §3.4.3, RL-02).

Enfin, le **découpage modulaire** : les dimensions de conception divergeaient (un module `Ged` vs deux modules
`GedCatalog` + `GedIndex`). Deux modules imposeraient une dépendance `Contracts → Contracts` inter-modules, alourdiraient
la surface NetArchTest et s'éloigneraient du pattern réel du repo (un module porte plusieurs schémas : `Documents`
porte `documents` + `archive_entries` + `document_events`). F19 §2.1 tranche : **un seul module** `Liakont.Modules.Ged`.

## Décision

### 1. UN module `Liakont.Modules.Ged` (couches Stratum), TROIS schémas PostgreSQL (F19 §2.1, D7)

**Un seul module racine `Liakont.Modules.Ged`** (couches Stratum standard `Contracts`/`Domain`/`Application`/
`Infrastructure`/`Web` + `MODULE.md`/`INVARIANTS.md`/`SCENARIOS.md`, module-rules §11). Il porte **trois schémas
PostgreSQL** :

- **`ged_catalog`** (définitions : axes, types d'entités, vocabulaires) — **base tenant** ;
- **`ged_index`** (instances : documents, entités, liens, recherche, graphe) — **base tenant** ;
- **`ged_ingestion`** (registre d'ingestion GED + outbox GED) — **base SYSTÈME**, co-localisé avec l'outbox, à
  l'identique de `ingestion.received_documents` (`V004`). Détaillé par **ADR-0034** (canal d'ingestion) ; cet ADR ne
  fait que le **situer**, car c'est la seule façon d'écrire **atomiquement** le registre + l'événement
  `ManagedDocumentReceivedV1` dans une même transaction (il n'existe pas de 2PC entre deux bases PG).

Motifs (F19 §2.1) : moins de surface NetArchTest et **pas de `Contracts → Contracts`** inter-modules ; alignement
sur le pattern réel du repo (un module = plusieurs schémas) ; alignement sur la doctrine « abstraction d'abord,
plug-in/module séparé ensuite ». Le split `Ged.Catalog`/`Ged.Search` en sous-modules reste un **fast-follow** si la
recherche monte en charge indépendamment (décision ouverte **D7**, défaut pris : un module).

### 2. Entité documentaire NEUVE `ManagedDocument`, distincte de `Documents.Document` fiscal (F19 §3.1)

On **ne réutilise PAS** l'agrégat fiscal `Documents.Document` ; on crée `ManagedDocument` dans le module `Ged`. Les
deux ont des raisons d'être, des cycles de vie et des champs **disjoints** :

| Critère | `Documents.Document` (fiscal, existant) | `ManagedDocument` (GED, neuf) |
|---|---|---|
| Raison d'être | Émission e-invoicing/e-reporting | Indexation/recherche/classement métier |
| Cycle de vie | Machine à états fermée `Detected→…→Issued` | Statut documentaire libre (`draft`/`indexed`/`archived`/`deferred`) |
| Champs | Figés EN 16931 (BT-1, montants `decimal`) | Quelconques, **portés par des axes** |
| Périmètre | Facture/avoir émis | Tout objet métier |
| Frontière | Ne peut grossir sans toucher le cœur fiscal | Isolé du flux fiscal |

`ManagedDocument` est **distinct ET relié** par des **soft-links OPTIONNELS, SANS FK cross-schéma** (pattern
`reconciliation_queue.proposed_document_id`) : `fiscal_document_id` (→ `documents.documents.id`) et `archive_entry_id`
(→ `documents.archive_entries.id`). **L'identité primaire d'un `ManagedDocument` est sa propre clé GED** ; le lien
fiscal est l'**exception** (un document GED purement métier n'a aucune contrepartie fiscale), jamais la clé.

**PAS de duplication d'état fiscal (RL-22)** : pour un `ManagedDocument` fiscal-lié, les champs dérivables du fiscal
(état de la facture, montants) sont **projetés à la lecture** depuis `Documents.Contracts` (pattern API01c),
**jamais copiés** dans `managed_documents` ; **aucun abonnement** du module `Ged` à l'événement fiscal
`DocumentReceivedV1` (qui déclenche le pipeline d'émission). La fiche affiche « vue d'indexation, voir la fiche
fiscale ».

```sql
-- ged_index.managed_documents (extrait portant la décision — F19 §3.4.1)
id                 uuid NOT NULL,   -- attribué par le handler d'ingestion ; INSERT ... ON CONFLICT (id) DO NOTHING (idempotence, RL-04)
fiscal_document_id uuid,            -- soft-link OPTIONNEL → documents.documents.id (sans FK cross-schéma)
archive_entry_id   uuid,            -- soft-link → documents.archive_entries.id (paquet scellé), sans FK
archive_path       text,           -- '{année}/{mois}/{clé}/' fiscal OU '_ged/{kind}/...' GED (ADR-0033, §5.1 F19)
content_hash       text,           -- SHA-256 du contenu indexé ; SET-ONCE à l'archivage, jamais ré-écrit (cf. INV-ARCH-GED-2)
status             text NOT NULL DEFAULT 'draft',  -- 'draft'|'indexed'|'archived'|'deferred'
retention_class    text NOT NULL DEFAULT 'tenant_bounded'  -- 'legal_hold'|'tenant_bounded'|'erasable'
```

### 3. Méta-modèle TYPÉ et SOURCÉ, PAS un EAV (F19 §3.3, §3.4, §3.5)

#### 3.1 `ged_catalog` — définitions MUTABLES (config vivante, trace dans `catalog_change_log`)

`ged_catalog.axis_definitions` est la **clé typée** du modèle. Points non négociables repris fidèlement de F19
§3.3.1 : `data_type` **typé et contraint** (`'string'|'date'|'number'|'boolean'|'enum'|'entity'|'json'`),
`value_scale` (échelle décimale **portée par l'axe** : 2=EUR, 0=entier, null=brut, ∈ [0..9]), `is_confidential`
(masquage console/export/log/index/graphe), `retention_class` (`'legal_hold'|'tenant_bounded'|'erasable'`, RGPD).
Un axe `data_type='entity'` exige `target_entity_type_id` (CHECK symétrique), pointant vers le registre polymorphe.

```sql
CREATE TABLE IF NOT EXISTS ged_catalog.axis_definitions (
    id              uuid NOT NULL DEFAULT gen_random_uuid(),
    code            text NOT NULL,          -- clé machine stable, ex. 'acheteur', 'numero_lot'
    data_type       text NOT NULL,          -- 'string'|'date'|'number'|'boolean'|'enum'|'entity'|'json'
    target_entity_type_id uuid,             -- requis SSI data_type='entity'
    value_scale     int,                    -- échelle décimale déclarée (2=EUR, 0=entier) ; null=brut
    is_confidential boolean NOT NULL DEFAULT false,
    retention_class text,                   -- 'legal_hold'|'tenant_bounded'|'erasable' ; null = hérité du tenant
    is_multi_value  boolean NOT NULL DEFAULT false,
    -- … (is_required, is_searchable, is_facetable, unit, ordinal, is_active, created_at, updated_at)
    CONSTRAINT ck_axis_def_data_type CHECK (data_type IN
        ('string','date','number','boolean','enum','entity','json')),
    CONSTRAINT ck_axis_def_entity_target CHECK ((data_type = 'entity') = (target_entity_type_id IS NOT NULL)),
    CONSTRAINT ck_axis_def_scale CHECK (value_scale IS NULL OR (value_scale >= 0 AND value_scale <= 9))
);
```

> **Invariant montant (CLAUDE.md n°1)** : `value_number` (§3.3 ci-dessous) est un `decimal` C#, **jamais**
> `double`/`float`. L'échelle est **portée par l'axe** (`value_scale`), pas par la colonne polymorphe ; l'arrondi
> **commercial half-up** à cette échelle est appliqué par `ValueNormalizer` (Domain pur) **avant** insert et
> matérialisé dans `normalized_value`.

> **Ordre des migrations (RL-07)** : la FK `fk_axis_def_target_entity` référence `ged_catalog.entity_types`. DbUp
> ordonne par nom de ressource ; `axis_definitions` précède `entity_types` en ordre alphabétique → créer
> `entity_types` **avant** `axis_definitions` (nommage de fichiers ordonné) **ou** poser la FK en `ALTER TABLE`
> séparé. Gardé par une acceptance (« migrations sur base vierge, FK satisfaite »).

`ged_catalog.entity_types` est le **registre polymorphe** (code, label, `identity_key`, `is_confidential`,
`is_active`). `ged_catalog.axis_values` porte le vocabulaire d'un axe `enum` (FK ON DELETE CASCADE sur l'axe).
`ged_catalog.catalog_change_log` est **append-only** (audit de la config : `before_value`/`after_value` jsonb,
`operator_identity`) : les définitions sont mutables **avec trace**.

#### 3.2 `ged_index` — instances polymorphes (`entity_instances`, registre, PAS une table par type)

```sql
-- ged_index.entity_instances (extrait — F19 §3.4.2)
entity_type_id  uuid NOT NULL,     -- référence LOGIQUE → ged_catalog.entity_types.id (validée en Application, §3.8 F19)
identity_value  text,              -- valeur normalisée de la clé de résolution (ex. SIRET) ; null si pas de clé
canonical_id    uuid,              -- identité canonique après fusion de doublons ; null = canonique
attributes      jsonb,             -- présentation-only, JAMAIS recherché/facetté/traversé (INV-GED-04)
search_vector   tsvector           -- FTS d'entité inline (cardinalité faible) ; cf. ADR-0035 pour le document
```

**Garde-fou anti-EAV (INV-GED-04)** : `attributes jsonb` est **présentation-only** ; toute donnée interrogeable
est un **axe déclaré** (testé). Les mutations d'entités sont journalisées dans `entity_instance_change_log`
(append-only).

#### 3.3 Les TROIS tables de liens APPEND-ONLY PUR (pièce maîtresse anti-EAV)

`ged_index.document_axis_links` (document↔valeur d'axe), `ged_index.entity_relations` (entité↔entité, graphe),
`ged_index.document_entity_links` (document↔entité) sont **append-only purs** : **aucune colonne mutable**. La valeur
courante = la **dernière de la chaîne** (`supersedes_id` posé sur la **nouvelle** ligne, jamais d'UPDATE). Une
**vue `current_*`** par table sélectionne les lignes non superséedées et non rétractées.

```sql
-- ged_index.document_axis_links (extrait portant la décision — F19 §3.4.3)
managed_document_id uuid NOT NULL,
axis_id             uuid NOT NULL,                 -- → ged_catalog.axis_definitions.id (logique)
-- Une SEULE colonne de valeur typée renseignée selon axis.data_type — JAMAIS un 'value text' fourre-tout :
value_string        text,
value_number        numeric,                       -- decimal exact C# ; échelle portée par l'axe (value_scale)
value_date          date,
value_boolean       boolean,
value_entity_id     uuid,                          -- → ged_index.entity_instances.id (axe data_type='entity')
value_json          jsonb,
normalized_value    text,                          -- tri/facette/recherche (casefold, unaccent, ISO, decimal canonique)
source              text NOT NULL,                 -- 'agent'|'manual'|'ai'|'import'|'ocr' (provenance)
confidence_score    numeric,                       -- [0..1] ; null si déterministe
supersedes_id       uuid,                          -- → ligne remplacée (chaîne de révision)
is_retraction       boolean NOT NULL DEFAULT false,-- RÉTRACTATION append-only : retire la valeur courante sans la remplacer (RL-24)
CONSTRAINT ck_dal_value_or_retraction CHECK (
    ( NOT is_retraction AND
      (value_string IS NOT NULL)::int + (value_number IS NOT NULL)::int +
      (value_date IS NOT NULL)::int + (value_boolean IS NOT NULL)::int +
      (value_entity_id IS NOT NULL)::int + (value_json IS NOT NULL)::int = 1 )
    OR
    ( is_retraction AND supersedes_id IS NOT NULL AND
      value_string IS NULL AND value_number IS NULL AND value_date IS NULL AND
      value_boolean IS NULL AND value_entity_id IS NULL AND value_json IS NULL )
);

CREATE OR REPLACE VIEW ged_index.current_axis_links AS
    SELECT d.* FROM ged_index.document_axis_links d
    WHERE d.is_retraction = false                  -- une rétractation n'est PAS une valeur courante (RL-24)
      AND NOT EXISTS (SELECT 1 FROM ged_index.document_axis_links s WHERE s.supersedes_id = d.id);
```

`entity_relations` et `document_entity_links` suivent le **même** patron (chaînage `supersedes_id`,
`is_retraction` avec CHECK `ck_er_retraction` / `ck_del_retraction` exigeant `supersedes_id IS NOT NULL`, vues
`current_entity_relations` / `current_document_entity_links`). `entity_relations` ajoute `ck_er_no_self`
(`from_entity_id <> to_entity_id`). Ces vues `current_*` sont la **surface de lecture** consommée par la recherche
et la traversée de graphe d'**ADR-0035 — Recherche & index GED**.

**Les CINQ raisons pour lesquelles ce n'est PAS un EAV (F19 §3.5)** : (1) la **clé est une `AxisDefinition` déclarée
et typée** ; (2) la **valeur est rangée dans une colonne typée** (`value_number numeric`, `value_date date`…) → tri,
comparaison, facette, `decimal` exact ; (3) chaque lien porte sa **provenance** (`source`, `confidence_score`,
`operator_identity`) ; (4) les **entités** sont des objets de première classe (`entity_instances` +
`entity_relations`) ; (5) **append-only auditable** (liens ET entités journalisés) vs EAV écrasé en place.

### 4. FRONTIÈRE P1 : le registre polymorphe N'ABSORBE NI le fiscal NI le socle (F19 §3.3.2)

`entity_types` / `entity_instances` est un **registre polymorphe GED** qui ÉVITE de **répéter**, pour les entités
**GED**, le pattern « une table dédiée par type » qu'illustrent `Mandats.Mandant`/`Mandat` (module fiscal,
**ADR-0022 — Mandant/tiers de première classe, module Mandats**) et `Stratum.Modules.Party` (socle **vendoré**).
Il **NE remplace NI n'absorbe** ces entités : elles restent dans leurs modules respectifs, **inchangées** (règle
review n°14 frontières / n°20 socle vendoré). Une entité métier GED qui correspond à un mandant/tiers fiscal s'y
**réfère** par **soft-link** (id externe `external_ref` / `identity_value`), **jamais** en le ré-hébergeant. Toute
violation (réplication de `Mandant`/`Party` dans `entity_instances`, jointure SQL cross-schéma `ged_* → mandats.*`
ou `→` socle) est un **P1** (gardée par lint SQL cross-schéma, F19 §8).

### 5. Append-only par triggers (motif réutilisé à l'identique de `reject_archive_entry_mutation`)

Sur **`catalog_change_log`, `managed_document_change_log`, `entity_instance_change_log`, `document_axis_links`,
`entity_relations`, `document_entity_links`** : le motif **déjà en production** (`reject_archive_entry_mutation`,
`Documents/V005`) — fonction `RAISE EXCEPTION` + **DEUX triggers** : `BEFORE UPDATE OR DELETE FOR EACH ROW` **et**
`BEFORE TRUNCATE FOR EACH STATEMENT` (le `FOR EACH ROW` ne couvre **pas** le TRUNCATE, vecteur de purge en masse).
Opposable à **tout rôle** (y compris propriétaire).

```sql
CREATE OR REPLACE FUNCTION ged_index.reject_axis_link_mutation()
    RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'Les liens d''axe GED (ged_index.document_axis_links) sont append-only : '
        'une valeur erronée se REMPLACE par une nouvelle ligne chaînée (supersedes_id), jamais par UPDATE/DELETE (CLAUDE.md n.4).';
END; $$;
CREATE OR REPLACE TRIGGER trg_dal_append_only
    BEFORE UPDATE OR DELETE ON ged_index.document_axis_links
    FOR EACH ROW EXECUTE FUNCTION ged_index.reject_axis_link_mutation();
CREATE OR REPLACE TRIGGER trg_dal_no_truncate
    BEFORE TRUNCATE ON ged_index.document_axis_links
    FOR EACH STATEMENT EXECUTE FUNCTION ged_index.reject_axis_link_mutation();
```

**Révision = chaînage `supersedes_id`, jamais d'UPDATE.** Une correction d'erreur passe par une **nouvelle ligne**
(remplacement) ou une **rétractation** (`is_retraction=true` + `supersedes_id`, qui retire la valeur courante sans la
remplacer, RL-24). C'est l'application directe de CLAUDE.md n°4 (audit & coffre immuables, append-only).

### 6. Garde mono-valeur SOUS CONCURRENCE (RL-02) — pas d'index partiel, advisory lock / `FOR UPDATE`

Pour un axe `is_multi_value=false`, l'unicité « une seule valeur courante par (document, axe) » **ne peut PAS**
reposer sur un index partiel : la « courante » est un **calcul de chaîne** (`supersedes_id`), pas une colonne, et une
contrainte `EXCLUDE` n'est pas applicable. Elle est portée **côté Domain à l'écriture, sous garde de concurrence** :
avant de superséder un axe MONO, `AppendAxisLinkAsync` prend un
`pg_advisory_xact_lock(hashtext(managed_document_id::text || ':' || axis_id::text))` **ou** un
`SELECT … FOR UPDATE` sur la ligne `managed_documents` parente, **dans la même transaction**, puis insère la nouvelle
ligne.

**Sans cette garde**, deux écritures concurrentes sous READ COMMITTED (défaut du projet) superséderaient chacune la
même courante → **double valeur courante PERMANENTE et non réparable** (les triggers append-only interdisent tout
UPDATE/DELETE de réparation). La lecture reste garantie par la vue `current_axis_links`. **Couverture obligatoire =
un test CONCURRENT** (deux appends simultanés ⇒ une seule courante) ; **un test séquentiel serait un faux-vert**
(F19 §3.4.3, §8).

```csharp
// Ged.Application — IGedIndexUnitOfWork (F19 §3.7)
// INSERT append-only ; si axe mono avec valeur courante : prendre un advisory lock / FOR UPDATE
// (clé document+axe) AVANT de superséder la courante (supersedes_id), dans la MÊME transaction —
// jamais d'UPDATE (trigger l'interdit). Garde de concurrence anti double-courante (RL-02).
Task AppendAxisLinkAsync(DocumentAxisLink link, CancellationToken ct = default);
```

`SetAxisValueCommandHandler` enchaîne : (1) `IAxisCatalog.ResolveAsync(axisCode)` → refus si axe inconnu/inactif
(**jamais deviner**, DEFER) ; (2) `ValueNormalizer.Normalize(dataType, value_scale, rawValue)` (Domain pur) →
`(colonne typée, normalized_value)`, refus si la valeur ne correspond pas au `data_type` ; (3) UoW : **garde de
concurrence si axe mono**, supersession chaînée, append. **Toute requête est tenant-scopée par la connexion.**

### 7. Référence cross-schéma `ged_index → ged_catalog` validée côté Application (D6)

`entity_type_id` / `axis_id` sont des **références LOGIQUES validées côté Application** via `IAxisCatalog` /
`IEntityTypeCatalog` (symétrie inter-modules, cohérent `reconciliation_queue`). Une **FK intra-base** (même base
tenant) reste **possible mais NON retenue par défaut** (décision ouverte **D6**, défaut pris : validation
applicative). C'est une **option réversible** — passer en FK intra-base plus tard ne casse aucun invariant de cet
ADR (les deux schémas vivent dans la même base tenant).

## Invariants

- **INV-GED-01** — Méta-modèle **anti-EAV** : la clé est une `AxisDefinition` déclarée et typée, la valeur est rangée
  dans une **colonne typée** (jamais un `value text` fourre-tout), avec provenance et entités de première classe
  (§3.3, §3.5). `value_number` est `decimal`, échelle portée par l'axe, half-up via `ValueNormalizer` (CLAUDE.md n°1).
- **INV-GED-02** — **Append-only** des trois tables de liens (`document_axis_links`, `entity_relations`,
  `document_entity_links`) et des change-logs : UPDATE/DELETE/TRUNCATE rejetés (double trigger, tout rôle) ; révision
  **par chaînage `supersedes_id`**, jamais par UPDATE ; rétractation par `is_retraction` (+ `supersedes_id`).
- **INV-GED-03** — **Mono-valeur** (`is_multi_value=false`) garantie **côté Domain SOUS CONCURRENCE** (advisory lock /
  `FOR UPDATE`, même transaction) ; pas d'index partiel possible ; `current_axis_links` ne renvoie qu'une ligne.
  **Test concurrent obligatoire** (séquentiel = faux-vert).
- **INV-GED-04** — `attributes jsonb` des entités est **présentation-only** : jamais recherché, facetté ni traversé ;
  toute donnée interrogeable est un **axe déclaré** (testé).
- **INV-GED-08** — **Tenant-scope par connexion** : aucune colonne `tenant_id`/`company_id` dans `ged_catalog`/
  `ged_index` (l'isolation EST la connexion, ADR-0011 socle database-per-tenant) ; aucune requête cross-tenant ; jointure SQL cross-schéma
  `ged_* → documents.*`/`mandats.*` **interdite** (soft-link logique seulement). Exception documentée : le registre
  d'ingestion vit en base système (`ged_ingestion`, porte `tenant_id`) — détaillée par ADR-0034.

Invariants connexes posés par les sœurs et rappelés ici parce qu'ils pèsent sur le méta-modèle :
- **INV-ARCH-GED-1 / INV-ARCH-GED-2** (option C, ADR-0033) — un document GED-seul est rangé **write-once (WORM)** via
  `IArchiveStore`, **hors** chaîne de hashes fiscale, et **ne modifie ni n'étend** `documents.archive_entries` ;
  `content_hash` (§3.4.1) n'est qu'une **copie indexée** des octets write-once de référence, jamais l'ancre
  souveraine. Conséquence pour CET ADR : `managed_documents.content_hash` est **SET-ONCE** et ne porte aucune
  prétention probante autonome.

## Conséquences

**Positif** : un **vrai méta-modèle typé, sourcé, append-only** remplace tout EAV — `decimal` exact, facette, tri,
provenance et auditabilité sont structurels, pas optionnels. La GED indexe **n'importe quel** objet métier par
paramétrage tenant **sans figer un schéma** dans le produit et **sans toucher au flux fiscal** (`ManagedDocument`
isolé de `Documents.Document`). Le registre polymorphe évite la prolifération « une table par type » **sans** absorber
le fiscal ni le socle (soft-link). On **réutilise** des patrons en production (triggers WORM `Documents/V005`,
soft-link `reconciliation_queue`, UoW + trigger `TvaMapping`) — **aucun mécanisme transverse nouveau, aucun code
`Stratum.*` vendoré modifié**.

**À la charge du(des) lot(s) d'implémentation** (items GED de F19 §10) : **GED02** scaffold module + docs + trois
schémas vides ; **GED03a** migrations `ged_catalog` (`entity_types` **avant** `axis_definitions`, RL-07) + Domain
catalogue ; **GED03b** `ged_index` documentaire (`managed_documents`, `document_axis_links` + triggers + vue
`current_axis_links`) + `ValueNormalizer` ; **GED03c** `ged_index` graphe (`entity_instances`, `entity_relations`,
`document_entity_links` + vues `current_*`) ; **GED04** `SetAxisValueCommand` + UoW + **garde de concurrence axe
mono** (RL-02) ; **GED06** permissions GED dans `RolePermissionCatalog`. Tests exigés (F19 §8) : append-only
(UPDATE/DELETE/TRUNCATE rejetés sur les 3 tables de liens + 3 change-logs, base réelle), **mono-valeur concurrent**,
rétractation `current_*`, isolation cross-tenant (≥ 2 bases), golden mapping / DEFER, idempotence `ManagedDocument`,
lint SQL cross-schéma, NetArchTest `NotHaveDependencyOn(Ged.*)` sur le flux fiscal, **garde de généricité outillée**
(aucun littéral métier `{lot, vente, pv, encheres, adjudication, acheteur, bordereau}` dans `src/Modules/Ged/**` hors
tests/seed).

**Limite** : cet ADR ne grave **ni** le canal d'ingestion (registre `ged_ingestion`, `IngestedDocumentDto`,
événement — **ADR-0034**), **ni** la recherche/facettes/graphe et la projection asynchrone (**ADR-0035**), **ni**
l'archivage probant tiers / l'option C (`ISealedArchiveProvider`, `sealed_refs` — **ADR-0033**), **ni** le journal de
consultation (**ADR-0036**). Il n'écrit **aucun code** (livré à partir de GED02) et **ne tranche aucune décision
fiscale, légale ou probante**.

### Points NON TRANCHÉS (F19 §11 — défaut défendable pris, l'owner tranche, jamais inventé)

| # | Point | Défaut défendable PRIS | Owner |
|---|---|---|---|
| D6 | Référence `ged_index → ged_catalog` : FK intra-base **ou** validation applicative | **Validation applicative** (symétrie inter-modules ; cohérent `reconciliation_queue`) ; FK intra-base réversible plus tard sans casser cet ADR | Archi |
| D7 | **Découpage** : un module `Ged` **ou** split `Ged.Catalog`/`Ged.Search` | **UN module `Ged`** (3 schémas PG : `ged_catalog`/`ged_index` tenant + `ged_ingestion` système) en V1 ; split si montée en charge recherche prouvée | Archi |

Aucun de ces points ne stalle le dev : ce sont des **défauts d'architecture réversibles**, pas des gates. D6/D7 sont
internes au module ; ils n'engagent aucune correctness fiscale ou probante.

## Alternatives rejetées

- **EAV `document_metadata(key, value text)`** : détruit le typage (un montant relu en `double` — viole CLAUDE.md
  n°1), la facette, le tri, la provenance et l'auditabilité (F19 §3.5). **Rejetée** — `axis_definitions` typé +
  colonnes de valeur typées + provenance + append-only.
- **Deux modules `GedCatalog` + `GedIndex`** : impose une dépendance `Contracts → Contracts` inter-modules, alourdit
  NetArchTest et s'éloigne du pattern réel (`Documents` porte plusieurs schémas). **Rejetée** (D7) — un seul module
  `Liakont.Modules.Ged` à trois schémas.
- **Colonne `value` fourre-tout** (une seule colonne text pour toutes les valeurs) : même perte de typage que l'EAV ;
  un montant cesse d'être `decimal`. **Rejetée** — une colonne de valeur **par type** (`value_string`/`value_number`/
  `value_date`/`value_boolean`/`value_entity_id`/`value_json`), exactement une renseignée (CHECK
  `ck_dal_value_or_retraction`).
- **Index partiel pour garantir le mono-valeur** : impossible, la « courante » est un calcul de chaîne
  (`supersedes_id`), pas une colonne ; `EXCLUDE` inapplicable. Un index partiel laisserait passer la **double valeur
  courante permanente** sous concurrence READ COMMITTED. **Rejetée** — garde Domain advisory lock / `FOR UPDATE` +
  test concurrent (RL-02).
- **Une table codée en dur PAR type d'entité GED** (`acheteurs`, `chantiers`…) : fige un vocabulaire métier dans le
  produit (viole la généricité, CLAUDE.md n°7) et multiplie les migrations à chaque type tenant. **Rejetée** —
  registre polymorphe `entity_types` / `entity_instances`.
- **Absorber `Mandats.Mandant`/`Mandat` ou `Stratum.Modules.Party` dans `entity_instances`** : viole la frontière
  inter-modules (règle review n°14) et modifie/double silencieusement le socle vendoré (règle n°20). **Rejetée** —
  soft-link par id externe, entités fiscales/socle inchangées dans leurs modules (§4, ADR-0022).
- **Dupliquer l'état fiscal dans `managed_documents`** (copier état/montants de la facture) : double source,
  divergence, et abonnement à `DocumentReceivedV1` (couple la GED au pipeline d'émission). **Rejetée** (RL-22) —
  projection à la lecture via `Documents.Contracts`, aucun abonnement à l'événement fiscal.
- **FK cross-schéma `ged_index → documents`** sur `fiscal_document_id`/`archive_entry_id` : couple les schémas et
  casse sous l'option C (un document GED-seul n'a aucune ligne `archive_entries`). **Rejetée** — soft-link **sans
  FK** (pattern `reconciliation_queue.proposed_document_id`).

## Références

- `docs/conception/F19-GED-Dynamique-Coffre-Fort.md` §2.1 (un module / trois schémas, D7), §2.3 (axes de plug-in),
  §3.1 (`ManagedDocument` neuf), §3.2 (db-per-tenant et exceptions), §3.3 (`ged_catalog`), §3.4 (`ged_index` instances
  & liens), §3.5 (« pas un EAV »), §3.6 (triggers append-only), §3.7 (UoW), §3.8 (référence cross-schéma), §7
  (frontières & règles non négociables), §8 (tests & DoD ; `INV-GED-01..12`, `INV-ARCH-GED-1..3`), §11 (D6/D7).
- ADR GED sœurs : **ADR-0033** (Coffre probant tiers / SAE comme 5ᵉ axe enfichable `ISealedArchiveProvider`, option C,
  fast-follow GED20), **ADR-0034** (Canal d'ingestion générique GED par agents, `IngestedDocumentDto` /
  `ManagedDocumentReceivedV1`, registre `ged_ingestion`, `IManagedExtractor`), **ADR-0035** (Recherche & index GED,
  `tsvector` derrière `IDocumentSearchIndex`, projection asynchrone reconstructible, graphe borné bidirectionnel —
  lit les vues `current_*`), **ADR-0036** (Journal de consultation GED append-only `ged_index.consultation_log`).
- ADR socle/Liakont : `docs/adr/socle/ADR-0011-database-per-tenant.md` (database-per-tenant) ;
  `docs/adr/ADR-0022-mandant-tiers-premiere-classe-module-mandats.md` (`Mandats.Mandant`/`Mandat`, frontière §4) ;
  `docs/adr/ADR-0007-serialisation-canonique-pivot.md` (sérialisation canonique).
- Code réel imité : `src/Modules/Documents/Migrations/V005__create_archive_entries_table.sql` (triggers WORM
  `reject_archive_entry_mutation` + no-truncate) ; `src/Modules/Reconciliation/Migrations/V002` (soft-link
  `proposed_document_id` sans FK) ; `src/Modules/TvaMapping/**` (UoW + trigger, `MappingChangeLog`) ;
  `src/Modules/Ingestion/Migrations/V004__create_received_documents_table.sql` (registre en base système).
- `blueprint.md` §2/§6/§7 ; `docs/architecture/module-rules.md` §6/§11.
