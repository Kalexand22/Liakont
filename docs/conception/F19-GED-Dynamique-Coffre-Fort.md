# F19 — GED dynamique & coffre-fort documentaire

> **Statut : 🟨 proposition de conception — NON RATIFIÉE.** Légende : ✅ tranché (dans cette spec) / ❓ NON TRANCHÉ (arbitrage hors code requis, owner désigné).
> **Lecteur visé** : expert DDD / socle Stratum / réglementation. Français.
> **Cadrage source** : aucune règle fiscale, légale ou probante n'est inventée ici. La GED est une **couche d'indexation métier** posée **AU-DESSUS** du coffre probant existant (module `Archive`, WORM, en production). Toute affirmation à portée probante/juridique non sourcée est marquée ❓ NON TRANCHÉ et renvoyée à un owner.
> **Sources socle vérifiées (code réel)** : `Archive.Contracts/IArchiveService.cs` (facture-spécifique : `ArchiveIssuedDocumentAsync`/`AddAddendumAsync` uniquement), `Archive.Domain/IArchiveStore.cs` + `ArchiveStoreCapabilities.cs` (stockage d'octets, `WriteAsync→Task` write-once idempotent — lève `ArchiveWriteConflictException`, capacités déclarées), `Documents/Migrations/V005__create_archive_entries_table.sql` (table possédée par **Documents**, triggers WORM `reject_archive_entry_mutation` + no-truncate), `Ingestion/Migrations/V004__create_received_documents_table.sql` (unique `(tenant_id, payload_hash)`, colonne `contract_version text NOT NULL`), `Reconciliation/Migrations/V002` (soft-link `proposed_document_id` sans FK), `TvaMapping` (`MappingTable`/`MappingRule`/`MappingChangeLog`), `blueprint.md` §2/§6/§7, `docs/architecture/module-rules.md`.

---

## 1. Positionnement & périmètre

### 1.1 Ce qu'est la GED Liakont

La GED est une **couche d'indexation métier + recherche multidimensionnelle + graphe de relations + ingestion générique par agents**, posée **au-dessus** du coffre-fort probant déjà en production. Elle apporte trois valeurs nouvelles, **sans toucher** au flux fiscal e-invoicing :

1. **indexer** par des **axes dynamiques déclarés par tenant** (méta-modèle, pas un schéma figé) ce qui est scellé dans le coffre ;
2. **relier** des documents à des **entités métier polymorphes** (acheteur, chantier, mandant…) et entre elles (graphe), pour répondre à « montre-moi tout ce qui touche le lot 42 » ;
3. **ingérer** des **documents métier arbitraires** (PV, contrats, courriers, bordereaux), pas seulement des factures EN 16931.

### 1.2 Ce que la GED N'EST PAS (frontières dures)

- **Pas un nouveau coffre probant.** La preuve de référence reste l'**intégrité produit** d'`Archive` : chaîne de hashes (`HashChain`) + ancrage RFC 3161 (`ITimestampAnchor`), **indépendante du backend** (blueprint règle 6). Un coffre tiers (Arkhineo / SAE NF Z42-013) est utilisé **EN PLUS**, jamais à la place (§5).
- **Pas une GED « enchères ».** Le métier enchères (lot/vente/PV/acheteur) n'est qu'un **exemple de paramétrage de tenant**. **Aucune** table/axe/entité « lot/vente/PV/adjudication » n'est codée en dur dans le produit (§7, garde outillée §8).
- **Pas un EAV.** Pas de `document_metadata(key,value)` : un vrai méta-modèle typé, sourcé, append-only (§3).
- **Pas un couplage au flux fiscal.** Le pipeline fiscal **ignore** la GED : aucune dépendance entrante `Pipeline/Validation/Transmission/Documents → Ged.*` (NetArchTest, §7).

### 1.3 Rapport au produit e-invoicing (découplage et upsell)

| Aspect | Garantie |
|---|---|
| Conformité fiscale | Identique **avec ou sans** GED installée. Désactiver la GED n'a aucun effet sur l'émission. |
| Couplage | La GED **observe** (événement d'intégration GED disjoint) et **lit** (`Archive.Contracts`, `Documents.Contracts`) ; elle n'injecte rien, ne valide rien, ne bloque rien. |
| Coffre partagé | La GED range ses documents-seuls write-once via `IArchiveStore` (octets WORM, espace `_ged/…`), **hors de la chaîne de hashes fiscale** (`archive_entries`) ; pour une facture, elle ne fait que **pointer** vers son paquet déjà scellé (soft-link, sans FK). Elle ne réécrit ni ne casse jamais la chaîne fiscale. |
| Activation | **Option / upsell par tenant** (paramétrage en base). Un tenant e-invoicing seul n'a ni module `Ged` chargé ni coffre tiers : zéro surcoût, zéro surface ajoutée. |

---

## 2. Vue d'ensemble d'architecture

### 2.1 Décision modulaire : UN module `Liakont.Modules.Ged`, trois schémas PG (✅, résout le conflit d'intégration P1)

Les dimensions de conception divergeaient (1 module `Ged` vs 2 modules `GedCatalog`+`GedIndex`). **Tranché : un seul module `Liakont.Modules.Ged`** (couches Stratum standard) avec **trois schémas PG** : `ged_catalog` (définitions) et `ged_index` (instances, liens, recherche) en **base tenant**, plus `ged_ingestion` (registre d'ingestion + outbox GED) en **base système** (RL-03, comme `ingestion.received_documents`). Motifs :

- moins de surface NetArchTest, **pas de `Contracts → Contracts`** inter-modules ;
- aligné sur le pattern réel du repo (un module porte plusieurs schémas/concepts : `Documents` porte `documents` + `archive_entries` + `document_events`) ;
- aligné sur la doctrine « abstraction d'abord, plug-in/module séparé ensuite » (historique `IArchiveStore` → S3). Le split `Ged.Search`/`Ged.Catalog` en sous-modules reste un **fast-follow** si la recherche monte en charge indépendamment (décision ouverte D7).

Le module livre obligatoirement `MODULE.md` / `INVARIANTS.md` / `SCENARIOS.md` (module-rules §11).

### 2.2 Nouveaux composants & extensions

| # | Élément | Statut | Localisation |
|---|---|---|---|
| 1 | Module `Liakont.Modules.Ged` (méta-modèle, ingestion GED, indexation, recherche, audit consultation) | **NEUF** | `src/Modules/Ged/` (schémas `ged_catalog`, `ged_index` tenant + `ged_ingestion` système) |
| 2 | DTO d'ingestion `IngestedDocumentDto` + `ManagedExtractorCapabilitiesDto` (`Agent.Contracts.Ged`) ; `IManagedExtractor` (`Agent.Core`, comme `IExtractor` — RL-15) | **NEUF** | `Liakont.Agent.Contracts.Ged` + `Liakont.Agent.Core` |
| 3 | Événement d'intégration `ManagedDocumentReceivedV1` (`ged.managed-document.received`) + `GedEventTypeRegistrar` | **NEUF** | `Ged.Contracts` (PAS `Ingestion.Contracts`, compile-visible des modules fiscaux — RL-17) ; outbox existant |
| 4 | Surface d'archivage générique `IGenericArchiveService.ArchiveManagedDocumentAsync` | **NEUF** | `Archive.Contracts` (additif, hash-neutre pour les factures) |
| 5 | Abstraction coffre tiers `ISealedArchiveProvider` + `SealedArchiveCapabilities` (5ᵉ axe) | **NEUF — fast-follow (GED20)** | `Archive.Domain` |
| 6 | Plug-in provider concret (`Archive/SealedProviders/Arkhineo`…) | **NEUF — fast-follow (GED20)** | plug-in (réf. `Archive.Contracts`/`Domain` uniquement) |
| 7 | Table append-only de réf. de scellement tiers `ged_index.sealed_refs` | **NEUF — fast-follow (GED20)** | module `Ged` ; réfère l'objet archivé par son chemin WORM (pas `archive_entries`) |
| 8 | Job `SealedReplicationTenantJob` + `SealedReplicationService` | **NEUF — fast-follow (GED20)** | `Archive.Infrastructure` (calqué `DailyAnchoringTenantJob`) |

### 2.3 Les axes de plug-in : le 5ᵉ axe = coffre probant tiers

Axes existants : (1) source `IExtractor`, (2) PA `IPaClient`+`PaCapabilities`, (3) stockage archive `IArchiveStore`+`ArchiveStoreCapabilities`, (4) IdP. **5ᵉ axe (NEUF)** : coffre probant tiers / SAE via `ISealedArchiveProvider`+`SealedArchiveCapabilities` (§5.2). Le moteur de recherche **n'est PAS** promu en axe en V1 : abstraction interne `IDocumentSearchIndex` (PG `tsvector`), plug-in fast-follow seulement si un 2ᵉ backend est livré.

### 2.4 Schéma ASCII des flux (MVP de bout en bout)

```
[Agent ODBC lecture seule]  IExtractor (facture)  ──▶  canal FISCAL (inchangé)  ──▶  PivotDocumentDto
        │                   IManagedExtractor (GED) ──▶  canal GED (NEUF)
        │  IngestedDocumentDto (champs BRUTS + forme déclarée, AUCUNE logique métier)
        ▼  POST /api/agent/v1/managed-documents/batch  (≤100, ManagedExtractorCapabilities)
[Plateforme — IngestManagedDocumentBatchHandler]
   ├─ GedCanonicalJson.Serialize → PayloadHasher.ComputeHash            (anti-doublon, RÉUTILISE primitive)
   ├─ GedIngestionDecision.Evaluate → Duplicate/Altered/New             (logique pure RE-COPIÉE dans Ged.Domain, RL-01)
   ├─ staging.WriteAsync(canonicalJson)  (Module Staging, AVANT la tx — invariant d'ordre ADR-0014 ; PAS atomique : pas de 2PC blob↔PG)
   ├─ [accepté, UNE transaction en BASE SYSTÈME (registre+outbox atomiques, RL-03)] :
   │     • INSERT ged_ingestion.ged_received_documents (registre GED, channel='ged')   (PAS de Document fiscal)
   │     • WriteEvent ManagedDocumentReceivedV1 (outbox)                (déclencheur DURABLE)
   │     • COMMIT
   └─ [post-commit, best-effort] IManagedDocumentIntake.Register   (fast-path : ne crée QUE managed_documents, ON CONFLICT — RL-04)
        │
[Consommateur ManagedDocumentReceivedV1 — module Ged]
   ├─ relit le pivot GED depuis le staging
   ├─ GedMapper.Map(profil tenant validé, ingested)  → MappedDocument  OU  DEFER (range, jamais devine)
   ├─ [si status≠'indexed'] UPSERT ManagedDocument (id du handler ; ON CONFLICT DO NOTHING) + liens, puis status='indexed' (MÊME tx) — un replay voit 'indexed' et no-op (RL-04)
   ├─ IGenericArchiveService.ArchiveManagedDocumentAsync(...) → rangement write-once via IArchiveStore (_ged/…, HORS chaine fiscale ; §5.1)
   └─ projette le search_vector vers document_search (recalcul asynchrone ; PAS sur le pivot — §6.1/§6.3)

[Recherche]  /ged/recherche  ──▶  IDocumentSearchIndex (PG tsvector + facettes) + traversée de graphe bornée
```

> **NB intégration** : le pont « une facture fiscale doit aussi apparaître en GED » est un **consommateur dédié** (qui crée un `ManagedDocument` soft-linké au `documents.documents`), **jamais** un abonnement du module `Ged` à l'événement fiscal `DocumentReceivedV1` (qui déclenche le pipeline d'émission).

---

## 3. Méta-modèle de données

### 3.1 Décision : entité documentaire NEUVE `ManagedDocument`, distincte de `Documents.Document` (✅)

On **ne réutilise PAS** l'agrégat fiscal `Documents.Document` ; on crée `ManagedDocument` dans le module `Ged`.

| Critère | `Documents.Document` (fiscal, existant) | `ManagedDocument` (GED, neuf) |
|---|---|---|
| Raison d'être | Émission e-invoicing/e-reporting | Indexation/recherche/classement métier |
| Cycle de vie | Machine à états fermée `Detected→…→Issued` | Statut documentaire libre (`draft/indexed/archived`) |
| Champs | Figés EN 16931 (BT-1, montants `decimal`) | Quelconques, **portés par des axes** |
| Périmètre | Facture/avoir émis | Tout objet métier |
| Frontière | Ne peut grossir sans toucher le cœur fiscal | Isolé du flux fiscal |

`ManagedDocument` est **distinct ET relié** : `fiscal_document_id` (soft-link optionnel vers `documents.documents`, pattern `reconciliation_queue.proposed_document_id`) et `archive_entry_id` (soft-link vers `documents.archive_entries`). **L'identité primaire d'un `ManagedDocument` est sa propre clé GED** ; le lien fiscal est l'**exception**, jamais la clé (un document GED purement métier n'a aucune contrepartie fiscale). **Pas de duplication d'état fiscal (RL-22)** : pour un `ManagedDocument` fiscal-lié, les champs dérivables du fiscal (état de la facture, montants) sont **projetés à la lecture** depuis `Documents.Contracts` (pattern API01c), **jamais copiés** dans `managed_documents` ; aucun abonnement du module `Ged` à `DocumentReceivedV1` — la fiche affiche « vue d'indexation, voir la fiche fiscale ».

### 3.2 Règle db-per-tenant et exceptions documentées (✅, corrige la sur-généralisation)

Par **défaut**, aucune colonne `tenant_id`/`company_id` : l'isolation EST la connexion (`IConnectionFactory` route vers la base du tenant). **Exceptions documentées, et seulement celles-ci** : (a) le **registre d'ingestion GED vit en BASE SYSTÈME** (schéma `ged_ingestion`), co-localisé avec l'outbox, exactement comme `ingestion.received_documents` (qui vit en base système — `PostgresReceivedDocumentUnitOfWork`, `V004`) : c'est la **seule** façon d'écrire **atomiquement** le registre + l'événement `ManagedDocumentReceivedV1` dans une **même transaction** (il n'existe pas de 2PC entre deux bases PG). Il porte donc `tenant_id` (résolu par slug à l'ingestion). L'**index GED** (`ged_catalog`/`ged_index`, base tenant) est peuplé **en aval** par le consommateur de l'événement (RL-03). (b) aucune vue Supervision cross-tenant n'est prévue sur la GED en V1 → **absence de `company_id` confirmée comme choix explicite**, pas comme axiome.

### 3.3 `ged_catalog` — définitions (MUTABLES, config vivante)

#### 3.3.1 `ged_catalog.axis_definitions`

```sql
CREATE TABLE IF NOT EXISTS ged_catalog.axis_definitions (
    id              uuid        NOT NULL DEFAULT gen_random_uuid(),
    code            text        NOT NULL,          -- clé machine stable, ex. 'acheteur', 'numero_lot'
    label           text        NOT NULL,          -- libellé opérateur (FR)
    data_type       text        NOT NULL,          -- 'string'|'date'|'number'|'boolean'|'enum'|'entity'|'json'
    target_entity_type_id uuid,                     -- requis SSI data_type='entity'
    value_scale     int,                            -- échelle décimale déclarée d'un axe number (2=EUR, 0=entier) ; null=brut
    is_multi_value  boolean     NOT NULL DEFAULT false,
    is_required     boolean     NOT NULL DEFAULT false,
    is_searchable   boolean     NOT NULL DEFAULT true,   -- alimente le tsvector
    is_facetable    boolean     NOT NULL DEFAULT false,
    is_confidential boolean     NOT NULL DEFAULT false,  -- masquage console/export/log/index/graphe (§6.5)
    retention_class text,                            -- 'legal_hold' | 'tenant_bounded' | 'erasable' (§7, RGPD) ; null = hérité du tenant
    unit            text,                            -- 'EUR','m2'… (informatif)
    ordinal         int         NOT NULL DEFAULT 0,
    is_active       boolean     NOT NULL DEFAULT true,   -- désactivation logique (jamais DELETE d'un axe utilisé)
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz,
    CONSTRAINT pk_axis_definitions PRIMARY KEY (id),
    CONSTRAINT uq_axis_definitions_code UNIQUE (code),
    CONSTRAINT fk_axis_def_target_entity FOREIGN KEY (target_entity_type_id)
        REFERENCES ged_catalog.entity_types (id),
    CONSTRAINT ck_axis_def_data_type CHECK (data_type IN
        ('string','date','number','boolean','enum','entity','json')),
    CONSTRAINT ck_axis_def_entity_target CHECK ((data_type = 'entity') = (target_entity_type_id IS NOT NULL)),
    CONSTRAINT ck_axis_def_scale CHECK (value_scale IS NULL OR (value_scale >= 0 AND value_scale <= 9))
);
```

> **Ordre des migrations (RL-07)** : la FK `fk_axis_def_target_entity` référence `ged_catalog.entity_types` (défini en §3.3.2). DbUp ordonne par **nom de ressource** ; `axis_definitions` précède `entity_types` en ordre alphabétique → la création de la FK échouerait si chaque table est dans un fichier séparé non ordonné. Remède : créer `entity_types` **avant** `axis_definitions` (nommage de fichiers garantissant l'ordre) **ou** poser la FK en **`ALTER TABLE` séparé** après les deux `CREATE`. Gardé par une acceptance GED03 (« migrations sur base vierge, FK satisfaite »).

> **Invariant montant (CLAUDE.md n°1)** : `value_number` (§3.4.3) est un `decimal` C#, **jamais** `double`/`float`. L'échelle est **portée par l'axe** (`value_scale`), pas par la colonne polymorphe ; l'arrondi half-up à cette échelle est appliqué par `ValueNormalizer` (Domain pur) **avant** insert et matérialisé dans `normalized_value`.

#### 3.3.2 `ged_catalog.entity_types` (registre polymorphe — au lieu d'une table codée en dur PAR type d'entité GED)

> **Frontière (ne PAS absorber le fiscal ni le socle) — P1** : ce registre évite de **répéter**, pour les entités **GED**, le pattern « une table dédiée par type » qu'illustrent `Mandats.Mandant`/`Mandat` (module fiscal, ADR-0022) et `Stratum.Modules.Party` (socle **vendoré**). Il **ne remplace ni n'absorbe** ces entités existantes — elles restent dans leurs modules respectifs, inchangées (règles review n°14 frontières / n°20 socle). Une entité métier GED qui correspond à un mandant/tiers fiscal s'y **réfère** par soft-link (id externe), jamais en le ré-hébergeant.

```sql
CREATE TABLE IF NOT EXISTS ged_catalog.entity_types (
    id              uuid        NOT NULL DEFAULT gen_random_uuid(),
    code            text        NOT NULL,           -- ex. 'acheteur','chantier'
    label           text        NOT NULL,
    identity_key    text,                            -- clé de résolution d'identité (§4.4), ex. 'siret' ; null = pas de dédup auto
    is_confidential boolean     NOT NULL DEFAULT false,  -- §6.5 : entité/relation confidentielle non traversable sans droit
    is_active       boolean     NOT NULL DEFAULT true,
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz,
    CONSTRAINT pk_entity_types PRIMARY KEY (id),
    CONSTRAINT uq_entity_types_code UNIQUE (code)
);
```

#### 3.3.3 `ged_catalog.axis_values` (vocabulaire d'un axe `enum`)

```sql
CREATE TABLE IF NOT EXISTS ged_catalog.axis_values (
    id        uuid NOT NULL DEFAULT gen_random_uuid(),
    axis_id   uuid NOT NULL,
    code      text NOT NULL,
    label     text NOT NULL,
    ordinal   int  NOT NULL DEFAULT 0,
    is_active boolean NOT NULL DEFAULT true,
    CONSTRAINT pk_axis_values PRIMARY KEY (id),
    CONSTRAINT fk_axis_values_axis FOREIGN KEY (axis_id)
        REFERENCES ged_catalog.axis_definitions (id) ON DELETE CASCADE,
    CONSTRAINT uq_axis_values_axis_code UNIQUE (axis_id, code)
);
```

#### 3.3.4 `ged_catalog.catalog_change_log` (APPEND-ONLY — audit de la config)

Colonnes : `id, change_type text, axis_id uuid, entity_type_id uuid, before_value jsonb, after_value jsonb, operator_identity text, operator_name text, occurred_at timestamptz DEFAULT now()`. **Triggers WORM** (motif §3.6) : seule cette table du schéma `ged_catalog` est append-only (les définitions, elles, sont mutables avec trace ici).

### 3.4 `ged_index` — instances & liens

#### 3.4.1 `ged_index.managed_documents` (entité-pivot d'index, mutable sur ses méta-colonnes, journalisée)

```sql
CREATE TABLE IF NOT EXISTS ged_index.managed_documents (
    id                 uuid        NOT NULL,   -- attribué par le handler d'ingestion, porté dans ManagedDocumentReceivedV1 ; INSERT ... ON CONFLICT (id) DO NOTHING sur fast-path ET consommateur (idempotence, RL-04)
    title              text        NOT NULL,
    doc_kind           text,                    -- libellé métier libre (PAS un état fiscal)
    fiscal_document_id uuid,                    -- soft-link OPTIONNEL → documents.documents.id (sans FK cross-schéma)
    archive_entry_id   uuid,                    -- soft-link → documents.archive_entries.id (paquet scellé), sans FK
    archive_path       text,                    -- chemin du paquet ('{année}/{mois}/{clé}/' fiscal OU '_ged/{kind}/...' GED, §5.1)
    content_hash       text,                    -- SHA-256 du contenu indexé (dédup) ; SET-ONCE à l'archivage, jamais ré-écrit (toute mutation serait tracée par managed_document_change_log). Ancre d'intégrité de RÉFÉRENCE = les OCTETS write-once de IArchiveStore (vraiment immuables, option C) ; ce hash n'en est qu'une copie indexée (cf. INV-ARCH-GED-2)
    status             text        NOT NULL DEFAULT 'draft',  -- 'draft'|'indexed'|'archived'|'deferred'
    retention_class    text        NOT NULL DEFAULT 'tenant_bounded', -- §7 : 'legal_hold'|'tenant_bounded'|'erasable'
    -- PAS de search_vector ici : le FTS document vit dans la table dérivée document_search (§6.3) — foyer UNIQUE et reconstructible (évite la double-source)
    created_utc        timestamptz NOT NULL DEFAULT now(),
    updated_utc        timestamptz,
    CONSTRAINT pk_managed_documents PRIMARY KEY (id),
    CONSTRAINT ck_md_status CHECK (status IN ('draft','indexed','archived','deferred')),
    CONSTRAINT ck_md_retention CHECK (retention_class IN ('legal_hold','tenant_bounded','erasable'))
);
CREATE INDEX IF NOT EXISTS ix_md_fiscal ON ged_index.managed_documents (fiscal_document_id) WHERE fiscal_document_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_md_archive ON ged_index.managed_documents (archive_entry_id) WHERE archive_entry_id IS NOT NULL;
```

Les mutations des méta-colonnes (`title/status/doc_kind`) sont tracées dans `ged_index.managed_document_change_log` (append-only, mêmes triggers) : le claim d'auditabilité (§3.5) couvre alors **les liens ET l'entité-pivot**.

#### 3.4.2 `ged_index.entity_instances` (registre polymorphe — au lieu d'une table dédiée par type d'entité GED ; n'absorbe pas les entités fiscales/socle, cf. frontière §3.3.2)

```sql
CREATE TABLE IF NOT EXISTS ged_index.entity_instances (
    id              uuid        NOT NULL DEFAULT gen_random_uuid(),
    entity_type_id  uuid        NOT NULL,        -- référence LOGIQUE → ged_catalog.entity_types.id (validée en Application, §3.8)
    display_name    text        NOT NULL,
    identity_value  text,                        -- valeur normalisée de la clé de résolution (§4.4), ex. SIRET ; null si pas de clé
    canonical_id    uuid,                        -- identité canonique après fusion de doublons (§4.4) ; null = canonique
    external_ref    text,                        -- réf. source brute
    attributes      jsonb,                       -- présentation-only, JAMAIS recherché/facetté/traversé (INV-GED-04)
    search_vector   tsvector,
    is_active       boolean     NOT NULL DEFAULT true,
    created_utc     timestamptz NOT NULL DEFAULT now(),
    updated_utc     timestamptz,
    CONSTRAINT pk_entity_instances PRIMARY KEY (id)
);
CREATE INDEX IF NOT EXISTS ix_ei_type     ON ged_index.entity_instances (entity_type_id);
CREATE INDEX IF NOT EXISTS ix_ei_identity ON ged_index.entity_instances (entity_type_id, identity_value) WHERE identity_value IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_ei_search   ON ged_index.entity_instances USING gin (search_vector);
```

Mutations journalisées dans `entity_instance_change_log` (append-only). **Garde-fou anti-EAV** : `attributes jsonb` est **présentation-only** ; toute donnée interrogeable est un **axe déclaré** (invariant testé INV-GED-04, §8).

> **FTS d'entité — asymétrie ASSUMÉE vs §6.1** : `search_vector`/`ix_ei_search` sont **inline, maintenus EN PLACE** dans la même mutation (journalisée) que l'entité (cardinalité faible, libellés courts). Ce n'est **pas** le modèle dérivé/reconstructible de `document_search` (qui sort le FTS du pivot mutable pour le rendre rebuild-able, §6.1/§6.3) : le FTS d'entité n'est **pas** revendiqué reconstructible-par-rebuild en V1. S'il doit le devenir (volume), il migre vers une table dérivée `entity_search` (fast-follow), exactement comme `document_search`.

#### 3.4.3 `ged_index.document_axis_links` (APPEND-ONLY PUR — pièce maîtresse anti-EAV)

Résolution du conflit interne `is_active`/trigger : **append pur, pas de colonne mutable**. La valeur courante = la **dernière de la chaîne** (`supersedes_id` posé sur la **nouvelle** ligne, jamais d'UPDATE).

```sql
CREATE TABLE IF NOT EXISTS ged_index.document_axis_links (
    id                  uuid        NOT NULL DEFAULT gen_random_uuid(),
    seq                 bigint      GENERATED ALWAYS AS IDENTITY,   -- ordre déterministe (monotone, append-compatible)
    managed_document_id uuid        NOT NULL,
    axis_id             uuid        NOT NULL,                       -- → ged_catalog.axis_definitions.id (logique)
    -- Une seule colonne de valeur typée renseignée selon axis.data_type — JAMAIS un 'value text' fourre-tout :
    value_string        text,
    value_number        numeric,                                   -- decimal exact C# ; échelle portée par l'axe (value_scale)
    value_date          date,
    value_boolean       boolean,
    value_entity_id     uuid,                                      -- → ged_index.entity_instances.id (axe data_type='entity')
    value_json          jsonb,
    normalized_value    text,                                      -- tri/facette/recherche (casefold, unaccent, ISO, decimal canonique)
    source              text        NOT NULL,                      -- 'agent'|'manual'|'ai'|'import'|'ocr'
    confidence_score    numeric,                                   -- [0..1] ; null si déterministe
    supersedes_id       uuid,                                      -- → id de la ligne que celle-ci remplace (chaîne de révision)
    is_retraction       boolean     NOT NULL DEFAULT false,        -- RÉTRACTATION append-only : retire la valeur courante sans la remplacer (RL-24)
    created_utc         timestamptz NOT NULL DEFAULT now(),
    operator_identity   text,                                      -- présent si source='manual'
    CONSTRAINT pk_document_axis_links PRIMARY KEY (id),
    CONSTRAINT ck_dal_source CHECK (source IN ('agent','manual','ai','import','ocr')),
    CONSTRAINT ck_dal_confidence CHECK (confidence_score IS NULL OR (confidence_score BETWEEN 0 AND 1)),
    CONSTRAINT ck_dal_value_or_retraction CHECK (
        -- exactement 1 valeur typée si lien normal ; 0 valeur + supersedes_id obligatoire si rétractation (RL-24) :
        ( NOT is_retraction AND
          (value_string IS NOT NULL)::int + (value_number IS NOT NULL)::int +
          (value_date IS NOT NULL)::int + (value_boolean IS NOT NULL)::int +
          (value_entity_id IS NOT NULL)::int + (value_json IS NOT NULL)::int = 1 )
        OR
        ( is_retraction AND supersedes_id IS NOT NULL AND
          value_string IS NULL AND value_number IS NULL AND value_date IS NULL AND
          value_boolean IS NULL AND value_entity_id IS NULL AND value_json IS NULL )
    )
);
-- « Valeur courante » = lignes qui ne sont superséedées par aucune autre :
CREATE OR REPLACE VIEW ged_index.current_axis_links AS
    SELECT d.* FROM ged_index.document_axis_links d
    WHERE d.is_retraction = false                                  -- une rétractation n'est PAS une valeur courante (RL-24)
      AND NOT EXISTS (SELECT 1 FROM ged_index.document_axis_links s WHERE s.supersedes_id = d.id);
CREATE INDEX IF NOT EXISTS ix_dal_doc        ON ged_index.document_axis_links (managed_document_id);
CREATE INDEX IF NOT EXISTS ix_dal_supersedes ON ged_index.document_axis_links (supersedes_id) WHERE supersedes_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_dal_axis_norm  ON ged_index.document_axis_links (axis_id, normalized_value);
```

> **Mono-valeur (`is_multi_value=false`) — garde de concurrence obligatoire (RL-02)** : l'unicité « une seule valeur courante par (document, axe) » **ne peut PAS** reposer sur un index partiel (colonne mutable sous append-only pur). Elle est portée **côté Domain à l'écriture, sous garde de concurrence** : avant de superséder un axe MONO, `AppendAxisLinkAsync` prend un `pg_advisory_xact_lock(hashtext(managed_document_id::text || ':' || axis_id::text))` **ou** un `SELECT … FOR UPDATE` sur la ligne `managed_documents` parente, **dans la même transaction**, puis insère la nouvelle ligne (`supersedes_id`). **Sans cette garde**, deux écritures concurrentes sous READ COMMITTED (défaut du projet) superséderaient chacune la même courante → **double valeur courante PERMANENTE et non réparable** (les triggers append-only interdisent tout UPDATE/DELETE). La lecture reste garantie par la vue `current_axis_links`. Une contrainte `EXCLUDE` n'est pas applicable (la « courante » est un calcul de chaîne). **Couverture obligatoire = un test CONCURRENT** (deux appends simultanés ⇒ une seule courante) ; un test séquentiel serait un faux-vert (§8).

#### 3.4.4 `ged_index.entity_relations` (APPEND-ONLY — graphe entité↔entité)

```sql
CREATE TABLE IF NOT EXISTS ged_index.entity_relations (
    id               uuid        NOT NULL DEFAULT gen_random_uuid(),
    seq              bigint      GENERATED ALWAYS AS IDENTITY,
    from_entity_id   uuid        NOT NULL,
    to_entity_id     uuid        NOT NULL,
    relation_kind    text        NOT NULL,        -- libellé métier déclaré, ex. 'appartient_a','sous_traitant_de'
    relation_type    text        NOT NULL,        -- 'direct'|'inferred'|'extracted'|'inherited' (provenance)
    confidence_score numeric,
    supersedes_id    uuid,                         -- dévalidation par chaînage (append pur)
    is_retraction    boolean     NOT NULL DEFAULT false,   -- retrait append-only d'une relation erronée, symétrique de document_axis_links (RL-24)
    source           text        NOT NULL,
    created_utc      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_entity_relations PRIMARY KEY (id),
    CONSTRAINT ck_er_relation_type CHECK (relation_type IN ('direct','inferred','extracted','inherited')),
    CONSTRAINT ck_er_source CHECK (source IN ('agent','manual','ai','import','ocr')),
    CONSTRAINT ck_er_no_self CHECK (from_entity_id <> to_entity_id),
    CONSTRAINT ck_er_confidence CHECK (confidence_score IS NULL OR (confidence_score BETWEEN 0 AND 1)),
    CONSTRAINT ck_er_retraction CHECK (NOT is_retraction OR supersedes_id IS NOT NULL)   -- une rétractation désigne ce qu'elle retire (RL-24)
);
CREATE INDEX IF NOT EXISTS ix_er_from ON ged_index.entity_relations (from_entity_id, relation_kind);
CREATE INDEX IF NOT EXISTS ix_er_to   ON ged_index.entity_relations (to_entity_id, relation_kind);
-- Relations courantes = ni rétractées ni superséedées (consommée par la traversée §6.4, RL-24) :
CREATE OR REPLACE VIEW ged_index.current_entity_relations AS
    SELECT e.* FROM ged_index.entity_relations e
    WHERE e.is_retraction = false
      AND NOT EXISTS (SELECT 1 FROM ged_index.entity_relations s WHERE s.supersedes_id = e.id);
```

#### 3.4.5 `ged_index.document_entity_links` (APPEND-ONLY — document↔entité)

```sql
CREATE TABLE IF NOT EXISTS ged_index.document_entity_links (
    id                  uuid        NOT NULL DEFAULT gen_random_uuid(),
    seq                 bigint      GENERATED ALWAYS AS IDENTITY,
    managed_document_id uuid        NOT NULL,
    entity_id           uuid        NOT NULL,
    role                text        NOT NULL,       -- ex. 'acheteur','chantier'
    relation_type       text        NOT NULL,       -- 'direct'|'inferred'|'extracted'|'inherited'
    confidence_score    numeric,
    supersedes_id       uuid,
    is_retraction       boolean     NOT NULL DEFAULT false,   -- retrait append-only d'un lien doc↔entité erroné, symétrique (RL-24)
    source              text        NOT NULL,
    created_utc         timestamptz NOT NULL DEFAULT now(),
    operator_identity   text,
    CONSTRAINT pk_document_entity_links PRIMARY KEY (id),
    CONSTRAINT ck_del_relation_type CHECK (relation_type IN ('direct','inferred','extracted','inherited')),
    CONSTRAINT ck_del_source CHECK (source IN ('agent','manual','ai','import','ocr')),
    CONSTRAINT ck_del_confidence CHECK (confidence_score IS NULL OR (confidence_score BETWEEN 0 AND 1)),
    CONSTRAINT ck_del_retraction CHECK (NOT is_retraction OR supersedes_id IS NOT NULL)   -- une rétractation désigne ce qu'elle retire (RL-24)
);
CREATE INDEX IF NOT EXISTS ix_del_doc    ON ged_index.document_entity_links (managed_document_id, role);
CREATE INDEX IF NOT EXISTS ix_del_entity ON ged_index.document_entity_links (entity_id, role);
-- Liens doc↔entité courants = ni rétractés ni superséedés (consommés par la traversée §6.4, RL-24) :
CREATE OR REPLACE VIEW ged_index.current_document_entity_links AS
    SELECT d.* FROM ged_index.document_entity_links d
    WHERE d.is_retraction = false
      AND NOT EXISTS (SELECT 1 FROM ged_index.document_entity_links s WHERE s.supersedes_id = d.id);
```

### 3.5 Pourquoi ce n'est PAS un EAV `(key,value)`

(1) la **clé est une `AxisDefinition` déclarée et typée** ; (2) la **valeur est rangée dans une colonne typée** (`value_number numeric`, `value_date date`…) → tri, comparaison, facette, `decimal` exact ; (3) chaque lien porte sa **provenance** (`source`, `confidence_score`, `operator_identity`) ; (4) les **entités** sont des objets de première classe (`entity_instances` + `entity_relations`) ; (5) **append-only auditable** (liens ET entités journalisées) vs EAV écrasé en place.

### 3.6 Triggers append-only (motif réutilisé à l'identique)

Sur **`catalog_change_log`, `managed_document_change_log`, `entity_instance_change_log`, `document_axis_links`, `entity_relations`, `document_entity_links`** : motif **déjà en production** (`reject_archive_entry_mutation`) — fonction `RAISE EXCEPTION` + trigger `BEFORE UPDATE OR DELETE FOR EACH ROW` **et** trigger `BEFORE TRUNCATE FOR EACH STATEMENT` (le `FOR EACH ROW` ne couvre pas le TRUNCATE — vecteur de purge en masse). Opposable à **tout rôle** (y compris propriétaire). Exemple pour `document_axis_links` :

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

### 3.7 Pattern d'écriture (UoW Dapper + commande MediatR)

```csharp
// Ged.Application
public interface IGedIndexUnitOfWork : IAsyncDisposable
{
    Task UpsertManagedDocumentAsync(ManagedDocument doc, CancellationToken ct = default);
    // INSERT append-only ; si axe mono avec valeur courante : prendre un advisory lock / FOR UPDATE
    // (clé document+axe) AVANT de superséder la courante (supersedes_id), dans la MÊME transaction —
    // jamais d'UPDATE (trigger l'interdit). Garde de concurrence anti double-courante (RL-02).
    Task AppendAxisLinkAsync(DocumentAxisLink link, CancellationToken ct = default);
    Task AppendEntityLinkAsync(DocumentEntityLink link, CancellationToken ct = default);
    Task AppendRelationAsync(EntityRelation relation, CancellationToken ct = default);
    Task CommitAsync(CancellationToken ct = default);
}
public interface IGedIndexUnitOfWorkFactory { Task<IGedIndexUnitOfWork> BeginAsync(CancellationToken ct = default); }
```

`SetAxisValueCommandHandler` (Infrastructure) : (1) `IAxisCatalog.ResolveAsync(axisCode)` → refus si axe inconnu/inactif (**jamais deviner**) ; (2) `ValueNormalizer.Normalize(dataType, value_scale, rawValue)` (Domain pur) → `(colonne typée, normalized_value)`, refus si la valeur ne correspond pas au `data_type` ; (3) UoW : **garde de concurrence (advisory lock / `FOR UPDATE`) si axe mono**, supersession chaînée, append, recalcul du `search_vector` (asynchrone, §6), commit. **Toute requête est tenant-scopée par la connexion.**

### 3.8 Référence cross-schéma `ged_index → ged_catalog`

`entity_type_id`/`axis_id` sont des **références logiques validées côté Application** via `IAxisCatalog`/`IEntityTypeCatalog` (symétrie inter-modules, cohérent `reconciliation_queue`). Une FK intra-base (même base tenant) reste possible mais non retenue par défaut (décision ouverte D6).

---

## 4. Ingestion générique par agents + mapping déclaratif

### 4.1 Coexistence des deux canaux (DTO disjoints, un seul moteur)

`PivotDocumentDto` est **fermé, aligné EN 16931, hashé canoniquement** : le surcharger casserait la stabilité octet du hash fiscal. **Deux DTO disjoints, deux endpoints, un moteur partagé.**

| | Canal fiscal (inchangé) | Canal GED (NEUF) |
|---|---|---|
| DTO | `PivotDocumentDto` | `IngestedDocumentDto` |
| Endpoint | `POST /api/agent/v1/documents/batch` | `POST /api/agent/v1/managed-documents/batch` |
| Sortie | `Document` `Detected` → pipeline fiscal | `ManagedDocument` + liens (jamais de `Document` fiscal) |
| Événement | `DocumentReceivedV1` | `ManagedDocumentReceivedV1` |
| Hash | `PayloadHasher` sur JSON canonique pivot | `PayloadHasher` (même primitive) sur `GedCanonicalJson` |

### 4.2 `IngestedDocumentDto` (NEUF — nom figé, résout les 3 noms divergents)

DTO PUR dans `Liakont.Agent.Contracts.Ged` (netstandard2.0). L'agent **extrait brut et déclare** ; **toute interprétation vit sur la plateforme** (CLAUDE.md n°6). Symétrie pivot « champ absent → `null` ».

```csharp
namespace Liakont.Agent.Contracts.Ged;   // NEUF

public sealed class IngestedDocumentDto
{
    string SourceReference;                 // clé de réconciliation + altération (obligatoire)
    string DocumentType;                    // type dans la source, valeur BRUTE (jamais classé par l'agent)
    DateTime? SourceTimestampUtc;
    IngestedContentRef? Content;            // { ContentRef, MediaType, ByteLength, ContentHash } ; null si pas de binaire
    IReadOnlyDictionary<string,string> SourceFields;   // BRUT ; ÉMIS TRIÉ PAR CLÉ (ordinal) par GedCanonicalJson — sinon l'anti-doublon (tenant,hash) casse (RL-39). PAS un EAV plateforme
    IReadOnlyList<RawAxisHint> SourceAxes;       // { Path/Name, Values[] }
    IReadOnlyList<RawEntityHint> SourceEntities; // { Type, ExternalId, Display }
    IReadOnlyList<RawRelationHint> SourceRelations; // { Type, TargetExternalId, TargetType }
}
```

### 4.3 Réutilisation EXACTE de l'existant

| Brique existante | Réutilisation GED |
|---|---|
| `PayloadHasher.ComputeHash(string)` | **réutilisé tel quel** (SHA-256 des octets — ne porte AUCUN déterminisme) ; NEUF : `GedCanonicalJson.Serialize(IngestedDocumentDto)` bâti **sur `CanonicalJsonWriter`** (ordre figé, null omis, ASCII, enums par nom) + `SourceFields` trié par clé **(ordinal)** ; golden cross-runtime net48/.NET 10 (RL-39) |
| `DocumentIngestionDecision.Evaluate` (logique pure, vit dans `Ingestion.Domain`) | logique **RE-COPIÉE** dans `Ged.Domain` (`GedIngestionDecision`, record struct 3 cas : Duplicate/AcceptedAltered/AcceptedNew) — **PAS** une référence à `Ingestion.Domain` (frontière, RL-01) ; option (b) : primitive neutre `Common/Abstractions` par ADR |
| Registre append-only | **registre GED DÉDIÉ** `ged_ingestion.ged_received_documents` (cf. 4.3.1), **pas** `ingestion.received_documents` |
| Outbox / MediatR | **réutilisé** ; NEUF : événement `ManagedDocumentReceivedV1` écrit dans la même tx |
| Staging | **réutilisé** — dépendance explicite au **module `Liakont.Modules.Staging`** (`Staging.Contracts`), PAS au module Ingestion |
| `IIngestedPdfStore` | **non fondu** : un `IIngestedContentStore` SÉPARÉ pour le contenu GED (cf. 4.3.2) ; le pool de réconciliation reste un concern fiscal |

#### 4.3.1 Registre d'ingestion GED dédié, en BASE SYSTÈME (✅, résout RL-03 + le partage `received_documents`)

Le partage de `ingestion.received_documents` est **écarté** : son unique index `(tenant_id, payload_hash)` n'a pas de discriminant de canal, et deux sérialiseurs canoniques disjoints y créeraient de faux `Duplicate`/`AcceptedAltered` (régression sur le canal fiscal). On crée un **registre GED propre, en BASE SYSTÈME** (schéma `ged_ingestion`), **co-localisé avec l'outbox** pour que l'INSERT du registre **et** l'écriture de `ManagedDocumentReceivedV1` soient **atomiques** (RL-03 ; calqué `PostgresReceivedDocumentUnitOfWork`) :

```sql
CREATE TABLE IF NOT EXISTS ged_ingestion.ged_received_documents (
    id                  uuid        NOT NULL DEFAULT gen_random_uuid(),
    tenant_id           text        NOT NULL,                  -- BASE SYSTÈME (schéma ged_ingestion), co-localisé outbox — comme ingestion.received_documents (§3.2(a), RL-03)
    source_reference    text        NOT NULL,
    payload_hash        text        NOT NULL,
    managed_document_id uuid        NOT NULL,
    contract_version    text        NOT NULL,                  -- version du contrat Liakont.Agent.Contracts.Ged (4.3.3)
    received_at         timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_ged_received_documents PRIMARY KEY (id)
);
CREATE UNIQUE INDEX uq_ged_received_tenant_payload ON ged_ingestion.ged_received_documents (tenant_id, payload_hash);
CREATE INDEX ix_ged_received_tenant_source ON ged_ingestion.ged_received_documents (tenant_id, source_reference, received_at DESC);
```

Avantages : isolation totale des espaces de hash, pas de discriminant à rétro-fitter sur la table fiscale, **aucun `Document` fiscal créé** (le handler GED **n'appelle pas** `IDocumentIntake`). L'anti-doublon `(tenant, hash)` GED est strictement local.

#### 4.3.2 Contenu GED : `IIngestedContentStore` séparé (✅)

`IIngestedPdfStore` (pool de réconciliation, énuméré par TRK07, sémantique d'écrasement) **reste intact**. NEUF : `IIngestedContentStore.SaveContentAsync(tenantId, contentRef, Stream, mediaType)/OpenContentAsync/ExistsAsync`, **anti path-traversal conservé** (`SafeTenant` + nom de fichier nu + contrôle sous-racine). Sémantique : le **buffer agent** peut être écrasable ; le **binaire probant GED** est write-once **au coffre via Archive** (§5.1).

#### 4.3.3 Version de contrat GED

Le canal GED versionne son propre contrat (`Liakont.Agent.Contracts.Ged`) et écrit cette version dans `contract_version` du registre GED. (jamais une version fiscale.)

### 4.4 Résolution d'identité des entités (✅, comble le trou)

Sans résolution, « tous les documents de l'acheteur Dupont » retourne des résultats partiels. Mécanisme :

1. **Clé de réconciliation par `EntityType`** : `identity_key` déclaré au paramétrage (ex. `siret` pour une société). `identity_value` = valeur normalisée.
2. **Upsert idempotent** à l'ingestion : lookup `(entity_type_id, identity_value normalisé)` avant création (réutilise `ix_ei_identity`). Pas de clé → pas de dédup auto (création par observation, fusion manuelle possible).
3. **Fusion de doublons append-only** : NE PAS muter les liens existants. On pose `canonical_id` sur l'instance fusionnée (chaîne vers l'identité canonique) ; la lecture résout l'identité canonique. **Aucune fusion automatique** : c'est un **geste opéré journalisé** (une fusion erronée est irréversible sous append-only). On n'utilise qu'un `confidence_score` numérique propre à la GED (`GedMergeConfidence` si une échelle nommée est nécessaire) — **jamais** le type `MatchConfidence` de `Reconciliation.Domain` (frontière interdite, RL-18), et **pas** de seuil « High=auto ».

### 4.5 Mapping déclaratif générique (généralisation de `MappingRule` — noms et vocabulaire figés)

Sur la **plateforme**, **tenant-scopé, versionné, validé humainement** (jamais côté agent). Entités NEUVES (module `Ged`) :

| Domaine TVA (existant) | Domaine GED (NEUF, nom figé) | Rôle |
|---|---|---|
| `MappingTable` (`IsValidated`/`Invalidate()`) | `GedMappingProfile` | Profil d'un `documentType`, tenant-scopé, versionné, validé |
| `MappingRule` (lookup → triplet, sinon block) | `AxisMappingRule` / `EntityMappingRule` / `RelationMappingRule` | Source path + condition → axe/entité/relation cible |
| `TvaMapper.Map` → résultat ou **BLOCK** | `GedMapper.Map(profile, ingested)` → `MappedDocument` ou **DEFER** | Range les inconnus au lieu de deviner |
| `MappingChangeLog` (append-only) | `GedMappingChangeLog` (append-only) | Audit immuable des profils |

**Vocabulaire figé : `DEFER`, jamais `BLOCK`.** L'enjeu GED n'est pas fiscal : un `documentType` **sans profil**, ou un axe **obligatoire non résolu**, range le document en `deferred` (visible console), **jamais mappé au hasard, jamais rejeté en silence**. Le coffre reste alimentable (le binaire est un fait ; l'indexation peut suivre).

Exemple de profil tenant (métier « enchères » = **exemple de paramétrage**, jamais codé en dur) :

```jsonc
// Paramétrage TENANT (en base, versionné, validé) — exemple fictif, seedé en deployments/<demo>/
{
  "documentType": "PV_VENTE",
  "storagePolicy": "WormPlusIndex",
  "axes": [
    { "axis": "date_vente",     "source": "$.fields.date_cloture", "multi": false },
    { "axis": "acheteur",       "source": "$.entities[?type=='seller'].externalId", "multi": false },
    { "axis": "numero_lot",     "source": "$.fields.lots[*].num", "multi": true }
  ],
  "entities": [ { "type": "acheteur", "externalId": "$.fields.seller_id", "display": "$.fields.seller_name" } ],
  "relations": [ { "kind": "concerne", "targetType": "vente", "targetExternalId": "$.fields.sale_id" } ]
}
```

> **Invariant montant à l'ingestion (CLAUDE.md n°1)** : les valeurs d'axe restent des **chaînes BRUTES** côté agent ; toute interprétation numérique d'un axe `number` se fait sur la plateforme en `decimal`, arrondi half-up à `value_scale` (§3.3.1), **jamais** `double`/`float`.
> ❓ NON TRANCHÉ : langage de sélection (`$.fields…`). Reco : **JSONPath restreint** (chemins simples + filtre d'égalité), pas un moteur d'expression (pas de calcul dans le mapping).
> **Parsing des `SourceFields` bruts** (date, nombre) : déclaré par profil (format source attendu), **DEFER si ambigu** (séparateur décimal, format de date local) — jamais deviner (cf. leçon ODBC DateTime).

### 4.6 Côté agent — capacités & extracteur (add-only, contrat non cassé)

`ManagedExtractorCapabilitiesDto` (NEUF, **disjoint** d'`ExtractorCapabilitiesDto`, DTO pur dans `Liakont.Agent.Contracts.Ged`) : `ProvidesManagedDocuments`, `ProvidesAxes`, `ProvidesEntities`, `ProvidesRelations`, `ProvidesBinaryContent`. `IManagedExtractor` (NEUF, **dans `Liakont.Agent.Core`** comme `IExtractor` — PAS dans `Agent.Contracts`, RL-15 ; séparé d'`IExtractor` pour préserver les invariants facture R1–R9) : `SourceName`, `Capabilities`, `IEnumerable<IngestedDocumentDto> ExtractManagedDocuments(from,to)` (streaming, lecture seule, idempotent), `Stream OpenContent(sourceReference)`. Un adaptateur peut implémenter l'un, l'autre, ou les deux. Tout est **additif** (ADR-0007) : aucun membre existant touché.

---

## 5. Coffre-fort : WORM Liakont souverain + abstraction coffre tiers à capacités

### 5.1 Archiver des documents GED arbitraires (surface générique, HORS chaîne fiscale)

> **Décision (redline RL-05) = option C.** Un document GED **purement métier** n'a aucune ligne `documents.documents`, or `documents.archive_entries.document_id` est `NOT NULL` + FK vers `documents.documents` (`V005:11,18-20`) et `IArchiveEntryStore.ReserveAsync(Guid documentId, …)` est non-nullable ; de plus la chaîne est **globale par tenant** (un seul `HeadSql`/verrou `0x41524348`), donc y insérer du GED **mélangerait** les maillons fiscaux et GED (une corruption GED casserait la vérification fiscale, et le `chain_hash` des factures suivantes dépendrait de l'activité GED). On **n'insère donc JAMAIS** un document GED-seul dans `archive_entries`. Le coffre fiscal (`archive_entries`, `HashChain`, `ArchiveVerifier`, ancrage RFC 3161) n'est **ni touché ni étendu**.

**RÉUTILISE inchangé** : `IArchiveStore` (rangement d'octets **write-once WORM**) et les exports streaming. **NE PARTAGE PAS** la chaîne fiscale : un document GED-seul est rangé via `IArchiveStore` sous un espace dédié (`_ged/…`, *Arborescence* ci-dessous), **hors** `archive_entries` et **sans ancrage RFC 3161 en V1**. Son intégrité locale = rangement write-once + `content_hash` indexé (§3.4.1) ; la valeur probante renforcée est **déférée au coffre tiers** (fast-follow GED20, §5.2). Une **facture**, elle, reste scellée par le flux fiscal existant ; la GED ne fait que la **pointer** (§3.1).

**NEUF** : `IArchiveService` est facture-spécifique (`ArchiveIssuedDocumentAsync` exige `DocumentId` FK, `PaResponseJson`, `Readable` facture…). On **n'étend pas** cette méthode ; on ajoute une **surface générique** :

```csharp
// Archive.Contracts — NEUF, additif. La facture reste sur ArchiveIssuedDocumentAsync (hash inchangé).
public interface IGenericArchiveService
{
    Task<ArchivePackageResult> ArchiveManagedDocumentAsync(GedArchivePackageRequest request, CancellationToken ct = default);
    // Addendum ciblant un paquet par sa clé GÉNÉRIQUE (cohérent avec l'arborescence _ged/...) :
    Task<ArchivePackageResult> AddManagedAddendumAsync(GedArchiveAddendumRequest request, CancellationToken ct = default);
}

public sealed record GedArchivePackageRequest(
    string ArchiveKind,                              // valeur produit GÉNÉRIQUE (jamais 'lot/vente'), nature métier = axe tenant
    string ArchiveKey,                               // clé d'arborescence (remplace DocumentNumber)
    DateOnly FiledOn,                                // remplace IssueDate
    IReadOnlyList<ArchiveAttachment> Contents,       // N pièces arbitraires (type existant) ; motif d'absence obligatoire si attendue absente
    string? ReadableHtml,                            // rendu lisible OPTIONNEL
    IReadOnlyList<ArchiveIndexAxis> IndexAxes);      // projection PLATE locale Archive (PAS le type GED DocumentAxisLink)

public readonly record struct ArchiveIndexAxis(string AxisCode, string? Value, bool IsConfidential);   // Value=null si IsConfidential : on ne fige JAMAIS une valeur confidentielle en clair dans le manifest WORM (RL-19)
```

> **Frontière (résout le P1 coffre-tiers)** : `Archive.Contracts` **ne référence JAMAIS** `DocumentAxisLink` (type du module GED). `IndexAxes` est une **projection plate locale** `ArchiveIndexAxis` ; la couche GED convertit ses `DocumentAxisLink` vers cette projection au point d'appel (pattern « projections locales, aucun Contracts→Contracts d'un autre module », API01c).
> **`IsConfidential` figé, valeur JAMAIS en clair (RL-19)** : pour un axe confidentiel, `index[]` ne porte que `AxisCode`+`IsConfidential` (`Value=null`). Le manifest WORM ne gèle **aucune** valeur confidentielle en clair (un axe requalifié confidentiel **après** scellement resterait sinon en clair, **irréversible** sous WORM). Les exports masquent/excluent de toute façon ; le **chiffrement au repos** d'une valeur confidentielle indexée reste **D9 ouvert**. Le coffre n'est pas un canal de contournement.

**Arborescence** : factures conservées sous `{année}/{mois}/{clé}/` (chaîne fiscale, **inchangée**) ; documents GED rangés via `IArchiveStore` sous **`_ged/{kind}/{année}/{mois}/{clé}/`** — un **espace d'octets WORM séparé, hors chaîne**. Sous l'option C, un document GED-seul n'a **aucune** ligne `documents.archive_entries` ; `FiscalControlExportService` n'énumère que la **chaîne fiscale** (`IArchiveEntryStore.GetChainAsync`), donc les paquets `_ged/…` en sont **structurellement absents** d'un export de contrôle fiscal (le filtre de période `FiscalControlExportService.cs:103-109` n'inclut de toute façon que les chemins `{année}/{mois}/…`). L'export de **réversibilité GED**, lui, énumère le coffre GED (`_ged/…`) et les inclut. L'intégrité d'un document GED en V1 = **rangement write-once WORM** + `content_hash` enregistré dans l'index GED (§3.4.1), **sans** chaîne ni ancrage fiscal.

> **INV-ARCH-GED-1 (option C)** : un document GED-seul est rangé **write-once (WORM)** via `IArchiveStore` (aucun chemin d'update/delete) mais **n'entre pas** dans la chaîne de hashes fiscale et **ne modifie ni n'étend** `documents.archive_entries` / `ArchiveVerifier`. La hash-neutralité des factures est donc **structurelle** (la GED ne partage pas la chaîne fiscale), pas seulement « prouvée par test ». Le test golden §8 vérifie qu'aucune migration ni surface GED ne touche la table ni le chemin d'écriture fiscal.

### 5.2 Brancher un coffre TIERS probant : `ISealedArchiveProvider` (✅, PAS un `IArchiveStore`, PAS un `IDocumentVault`)

> **Périmètre V1 (option C, redline RL-05/RL-26) :** les §§5.2 à 5.6 décrivent le **coffre tiers probant = fast-follow (lot GED20), NON livré en V1.** En V1, l'intégrité d'un document GED = **rangement write-once WORM** via `IArchiveStore` + `content_hash` indexé (§5.1) ; **aucune** valeur probante tierce n'est affirmée. Le port `ISealedArchiveProvider` et la table de référence ci-dessous sont posés par GED20 **avec le premier provider**, pas en V1 (évite le code dormant, RL-26).

Résolution du conflit des abstractions concurrentes : un SAE tiers **n'est pas** un stockage d'octets adressé par nous. Il **scelle** selon sa politique, **attribue** une référence opaque, **retourne** une preuve, et a un **cycle asynchrone**. Le forcer dans `IArchiveStore` (`WriteAsync→Task` write-once, lecture par chemin) serait faux. On crée une **abstraction sœur distincte à capacités** ; on **supprime** le `IDocumentVault` unifié des brouillons (il mélangeait stockage d'octets et scellement).

```csharp
// Archive.Domain — NEUF, sœur de IArchiveStore. Sémantique « scellement + référence + preuve (peut être pending) ».
public interface ISealedArchiveProvider
{
    SealedArchiveCapabilities Capabilities { get; }                          // jamais if (provider is Arkhineo)
    Task<SealOutcome> SealAsync(SealRequest request, CancellationToken ct);  // verse + réf + preuve (pending possible)
    Task<SealOutcome> PollAsync(string externalRef, CancellationToken ct);   // cycle asynchrone : pending -> sealed
    Task<byte[]> RetrieveProofAsync(string externalRef, CancellationToken ct);
}

public readonly record struct SealedArchiveCapabilities(
    bool SupportsSealing,
    bool SupportsQualifiedTimestamp,   // horodatage qualifié eIDAS
    bool ClaimsNfZ42013,               // le FOURNISSEUR revendique NF Z42-013 — déclaratif, ❓ NON TRANCHÉ (D1)
    bool SupportsRetrieval,
    bool IsAsynchronousSeal);
```

> **Frontière (P1)** : aucune fonctionnalité ne teste un provider concret. Le plug-in (`Archive/SealedProviders/Arkhineo`) ne référence que `Archive.Contracts`/`Domain`. Choix du provider = **config d'instance/tenant**. Le **secret** d'accès (clé API / certificat) est **chiffré par tenant en base** (jamais en clair, règle 10) ; le code n'embarque que des exemples fictifs (`config/exemples/`).

### 5.3 Stockage HYBRIDE : intégrité produit souveraine

Le coffre tiers (fast-follow GED20) est une **réplique scellée EN PLUS**. Le dépôt local de référence reste **souverain** : pour une **facture**, sa chaîne de hashes fiscale ; pour un **document GED**, son rangement write-once WORM (§5.1). Le versement tiers est un **second temps** ; sa preuve est rangée **write-once dans le même répertoire** (`_ged/…` ou paquet fiscal) que le paquet qu'elle prouve, donc conservée WORM et vérifiable hors-ligne. **En V1, sans coffre tiers, ce second temps n'existe pas.**

> **INV-ARCH-GED-2 (P1)** : pour une **facture**, la valeur probante de référence reste **chaîne de hashes + ancrage RFC 3161** (souveraine, inchangée). Pour un **document GED**, l'intégrité locale de référence est le **rangement write-once WORM** + `content_hash` indexé. Toute valeur probante renforcée (coffre tiers, NF Z42-013, ou chaîne GED dédiée ultérieure) est **déférée à un fast-follow** et **n'est jamais affirmée** tant que non livrée et confirmée (règle 3). Jamais bloquer le flux sur l'indisponibilité d'un coffre tiers (règle 8).

### 5.4 Référence de scellement tiers : `ged_index.sealed_refs` (fast-follow GED20 — référence l'objet archivé, PAS `archive_entries`)

Posée par **GED20** (pas en V1). Sous l'**option C**, un document GED-seul n'a **aucune** ligne `documents.archive_entries` : la référence de scellement ne peut donc **pas** être une FK vers `archive_entries`. La table vit dans le **module `Ged`** (schéma `ged_index`), append-only, et référence l'**objet archivé par son chemin WORM** (`archive_path` : paquet fiscal `{année}/…` ou GED `_ged/…`). On **écarte** toute colonne/FK sur `archive_entries` (incompatible avec un document GED-seul ET avec le trigger WORM qui rejette tout UPDATE). *(Ceci résout la **collision de schéma** pointée par RL-07 — la table sort du schéma `documents` déjà à double-V010 ; son numéro concret sera alloué par GED20 dans la séquence de migrations `ged_index` neuve, sans pression de collision.)*

```sql
-- ged_index.V0NN__create_sealed_refs_table.sql  (fast-follow GED20, schéma ged_index, WORM append-only)
CREATE TABLE IF NOT EXISTS ged_index.sealed_refs (
    id                  uuid        NOT NULL DEFAULT gen_random_uuid(),
    seq                 bigint      GENERATED ALWAYS AS IDENTITY,   -- ordre déterministe (« dernière ligne » sans ambiguïté)
    archive_path        text        NOT NULL,          -- chemin WORM de l'objet scellé ('{année}/…' fiscal OU '_ged/…') — PAS une FK archive_entries
    external_provider   text        NOT NULL,          -- identifiant de CAPACITÉ résolu via le registre (jamais une valeur libre saisie)
    external_archive_id text        NULL,              -- réf opaque du coffre tiers (null tant que pending)
    seal_status         text        NOT NULL,          -- 'pending' | 'sealed' | 'failed' | 'unsupported'
    sealed_at           timestamptz NULL,              -- horodatage RETOURNÉ par le coffre tiers
    proof_path          text        NULL,              -- chemin WORM de la preuve rapatriée (rangée write-once à côté du paquet)
    recorded_utc        timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_sealed_refs PRIMARY KEY (id),
    CONSTRAINT ck_sealed_refs_status CHECK (seal_status IN ('pending','sealed','failed','unsupported'))
);
CREATE INDEX ix_sealed_refs_path ON ged_index.sealed_refs (archive_path, seq DESC);
-- au plus une ligne 'sealed' par objet (idempotence du job, comme archive_anchors par tête) :
CREATE UNIQUE INDEX uq_sealed_refs_sealed ON ged_index.sealed_refs (archive_path) WHERE seal_status = 'sealed';
-- triggers reject_*_mutation (UPDATE/DELETE) + no_truncate : COPIE EXACTE du pattern archive_entries (§3.6).
```

> **État courant** = dernière ligne par `archive_path` selon **`seq`** (déterministe, jamais `recorded_utc`). Idempotence par **clé métier** (`archive_path` + provider), comme `archive_anchors` sur `(head, method)`. `external_provider` est l'**identifiant de capacité résolu via le registre** (toute ligne dont le provider n'est plus résolvable est traitée `SealClaimedNotVerifiable`, jamais ignorée).

### 5.5 Job de réplication (calqué `DailyAnchoringTenantJob`)

**(Fast-follow GED20, pas en V1.)** `SealedReplicationTenantJob : ITenantJob` (`Name => "archive.sealed-replication"`) — le fan-out par tenant est la seule responsabilité de `TenantJobRunner` (module-rules §6). `SealedReplicationService` (WORM-safe) : (1) court-circuit par capacité (`SupportsSealing == false` → ligne `unsupported`, jamais un faux vert) ; (2) idempotence (lecture de la dernière ligne) ; (3) `SealAsync` → si `IsAsynchronousSeal`, `pending` puis `PollAsync` au tour suivant ; (4) au `sealed` : `RetrieveProofAsync` → **preuve rangée write-once (WORM) à côté du paquet** via `IArchiveStore` → insère la ligne `sealed` avec `proof_path`.

### 5.6 Vérifieur & export (additifs)

**(Fast-follow GED20.)** Un rapport « scellement tiers » : la preuve étant rangée **write-once dans le coffre WORM** à côté du paquet, sa présence et son empreinte sont vérifiables ; statuts prudents `SealVerified` / `SealClaimedNotVerifiable` / `SealMissing` (sur le modèle `NotVerifiable` des ancrages : une preuve non re-vérifiable est **non vérifiable, jamais invalide**). Export : `references-coffre-tiers.json` **filtré sur exactement les `archive_path` retenus par l'export en cours** (jointure sur la sélection, jamais un dump global) ; notice : « scellé chez un tiers déclarant NF Z42-013, **non re-vérifié par Liakont** », jamais « conforme NF Z42-013 ».

> **INV-ARCH-GED-3 (P1)** : `ClaimsNfZ42013 == true` (déclaratif fournisseur) **n'autorise jamais à lui seul** un affichage/une notice « conforme NF Z42-013 ». Seul `SealVerified` **+** une spécification de vérification autonome ratifiée (D1) le permet. Tant que D1 non tranché : au plus « scellé chez un tiers déclarant ».

---

## 6. Recherche multidimensionnelle, portail, droits, audit applicatif

### 6.1 Peuplement de l'index : projection asynchrone (jamais double écriture, jamais l'événement fiscal)

L'index se peuple par **consommation de `ManagedDocumentReceivedV1`** (canal GED) et des événements de mapping/archivage GED — **jamais** par abonnement à `DocumentReceivedV1` (qui déclenche le pipeline fiscal). Le recalcul du `search_vector` est **asynchrone** (event handler), pour découpler latence d'ingestion et indexation. **Foyer UNIQUE du FTS document = la table dérivée `ged_index.document_search`** (§6.3) ; le pivot `managed_documents` ne porte **aucune** colonne `search_vector` (sinon double-source non réconciliable). Étant un **dérivé**, `document_search` peut être tronqué/reconstruit (DELETE+rebuild) sans violer la règle 4 — documenté pour éviter un faux-P1.

### 6.2 Recherche par axe + facettes (requête multi-axes correcte)

```sql
-- Documents portant 'acheteur'=@a ET 'numero_lot'=@l (conjonction multi-axes, robuste aux axes multi-valeurs) :
SELECT dal.managed_document_id
FROM   ged_index.current_axis_links dal
JOIN   ged_catalog.axis_definitions ad ON ad.id = dal.axis_id
WHERE  (ad.is_confidential = false OR @hasConfidentialRight)   -- prédicat de confidentialité OBLIGATOIRE (RL-31, anti-oracle)
GROUP BY dal.managed_document_id
HAVING count(DISTINCT CASE
         WHEN ad.code='acheteur'   AND dal.normalized_value=@a THEN 'a'
         WHEN ad.code='numero_lot' AND dal.normalized_value=@l THEN 'b' END) = 2;
```

> Le `count(DISTINCT code)` naïf donne un **faux positif** sur un axe multi-valeur ; on compte les **critères réellement satisfaits** (ou intersection de sous-requêtes par axe). **Facettes** : `count(*) GROUP BY (axis_id, normalized_value)` restreint aux axes `is_facetable`, **respectant strictement** la confidentialité (§6.5) — une facette sur un axe confidentiel masqué ne révèle jamais de comptes (anti-oracle). **Le prédicat `(ad.is_confidential = false OR @hasConfidentialRight)` est matérialisé DANS la requête, pas seulement en prose (RL-31)** ; ces SQL sont des esquisses dont ce prédicat est non négociable.

### 6.3 Recherche plein texte (PG `tsvector`, V1) et SOURCE du texte (✅, comble le trou)

`ged_index.document_search(managed_document_id uuid PK, search_vector tsvector, refreshed_utc timestamptz)` — **seul** foyer du `search_vector` document (le pivot `managed_documents` n'en porte pas, §3.4.1), index **GIN**. Requête `websearch_to_tsquery('french', @q)`, pondération `setweight` (titre=A, axes searchables=B). **Source du texte indexable** (explicite) : (1) documents portant un rendu lisible (factures via `ReadableDocumentRenderer`, ou `ReadableHtml` fourni) → extraction texte du HTML ; (2) PDF à couche texte → extraction de couche texte en V1, sinon **étiqueté « cherché sur métadonnées seulement »** ; (3) scan image → **OCR explicitement fast-follow**, document **clairement non-full-text en V1** (pas de faux « plein texte » silencieux). Distinction d'invariant : **recherche sur axes** (V1 garantie) vs **recherche sur contenu** (conditionnelle à la disponibilité d'une couche texte). Config `'french'` (FR-only aligné F10) ; multilingue = ❓ NON TRANCHÉ. **Provisionnement (NEUF — aucun `tsvector`/`unaccent` n'existe dans le repo aujourd'hui, à ne PAS présenter comme « réutilisé », RL-13)** : migration `CREATE EXTENSION IF NOT EXISTS unaccent` (droit superuser au moment de la migration) + **wrapper IMMUTABLE** de `unaccent()` (qui est STABLE) pour l'usage en colonne générée / expression d'index.

### 6.4 Traversée de graphe BIDIRECTIONNELLE et bornée (✅, corrige l'exemple unidirectionnel)

```sql
WITH RECURSIVE reach AS (
    SELECT @rootEntityId AS entity_id, 0 AS depth, ARRAY[@rootEntityId]::uuid[] AS path
  UNION ALL
    SELECT nxt.entity_id, r.depth + 1, r.path || r.entity_id
    FROM   reach r
    JOIN   ged_index.current_entity_relations er                                          -- exclut rétractées/superséedées (RL-24)
           ON (er.from_entity_id = r.entity_id OR er.to_entity_id = r.entity_id)          -- BIDIRECTIONNEL
    CROSS JOIN LATERAL (SELECT CASE WHEN er.from_entity_id = r.entity_id
                                    THEN er.to_entity_id ELSE er.from_entity_id END AS entity_id) nxt
    JOIN   ged_index.entity_instances ei ON ei.id = nxt.entity_id
    JOIN   ged_catalog.entity_types  et ON et.id = ei.entity_type_id
    WHERE  r.depth < @maxDepth                                                            -- borne dure (défaut 4, paramètre tenant)
      AND  NOT nxt.entity_id = ANY(r.path)                                                -- anti-cycle
      AND  (et.is_confidential = false OR @hasConfidentialRight)                          -- confidentialité héritée du type d'entité (RL-31)
)
SELECT DISTINCT del.managed_document_id, del.role, r.entity_id, r.depth
FROM   reach r
JOIN   ged_index.current_document_entity_links del ON del.entity_id = r.entity_id;        -- exclut rétractés/superséedés (RL-24)
```

> Le métier « enchères » (lot/vente/PV) n'est qu'un EXEMPLE : la traversée est **agnostique** (graphe générique). **Borne de profondeur obligatoire** (paramètre tenant, jamais infinie) = garde anti-DoS. **Pagination keyset** (jamais `OFFSET`, jamais chargement-tout en mémoire) ; le portail consomme une page déjà bornée côté SQL (pas le chargement-tout de `DeclaredListPage`). **Confidentialité dans la traversée (RL-31)** : la traversée lit les vues `current_*` (exclut rétractées/superséedées, RL-24) et **joint `entity_instances`→`entity_types`** pour exclure toute entité dont le `entity_type` est confidentiel sans le droit (`et.is_confidential = false OR @hasConfidentialRight`) — la confidentialité d'une **relation s'hérite des types d'entités à ses extrémités** (`entity_relations` n'a **pas** de colonne `is_confidential`). Le prédicat est **matérialisé dans le corps SQL** ci-dessus, pas seulement en prose ; le canal de fuite ne se déplace pas de l'axe vers le graphe.

### 6.5 Droits & confidentialité : masquage applicatif server-side (✅ ; chiffrement au repos = D9 ouvert, RL-19)

**RÉUTILISE** : claims `permission` projetés par `RolePermissionCatalog` (ADR-0017), `PermissionAuthorizationHandler`, `IActorContext`. **NEUF** : permissions **Liakont dédiées** `liakont.ged.read` / `liakont.ged.export` / `liakont.ged.confidential` — à **amender** dans la matrice `RolePermissionCatalog` (ADR-0017) : c'est une **matérialisation en CODE** (`Dictionary`/`const`), donc un amendement de code traçable (recompilation) — **pas** une règle fiscale inventée, et **pas** du paramétrage tenant en base ; jamais une permission socle accordée à un rôle Liakont (cf. FIX07c, RL-35). Tant que la matrice n'est pas amendée, **aucune permission GED n'est accordée** (bloquer plutôt qu'inventer).

Masquage **serveur** (jamais UI), étendu à tous les canaux :
- **axes** `is_confidential` : omis de la projection, **non filtrables/comptables** en facette sans le droit (anti-oracle) ;
- **graphe** : un `entity_type`/`relation` marqué confidentiel n'est ni retourné ni traversable sans le droit (le canal de fuite ne se déplace pas de l'axe vers le graphe) ;
- **plein texte** : les axes confidentiels sont **exclus** du `search_vector` partagé (V1) ;
- **export** : les axes `IsConfidential` figés dans le manifest (§5.1) sont masqués/exclus des exports ;
- **log** : `detail` ET `query_text` masqués/hachés si confidentiels (§6.6) ;
- **au repos** : ❓ NON TRANCHÉ — chiffrement des valeurs d'axes confidentielles (owner sécurité, D9).

### 6.6 Audit applicatif de consultation (append-only, base TENANT)

Table NEUVE en **base tenant, schéma `ged_index`** (donnée métier tenant-scopée, comme `document_events`), **routée par `IConnectionFactory`** — **JAMAIS** via `ISystemConnectionFactory` (qui écrirait dans l'`audit.field_changes` partagé = fuite cross-tenant). Distincte de l'audit socle de *mutations*.

```sql
CREATE TABLE IF NOT EXISTS ged_index.consultation_log (
    id            uuid        NOT NULL DEFAULT gen_random_uuid(),
    occurred_utc  timestamptz NOT NULL DEFAULT now(),
    actor_id      text        NOT NULL,
    action        text        NOT NULL,   -- 'search'|'view_document'|'explore_entity'|'export'|'open_archive'
    managed_document_id uuid,
    entity_id     uuid,
    query_text    text,                    -- masqué/haché si axe confidentiel ciblé (§6.5)
    result_count  int,
    detail        jsonb,                   -- critères/facettes, confidentiels masqués
    correlation_id uuid,
    CONSTRAINT pk_consultation_log PRIMARY KEY (id)
);
CREATE INDEX ix_consultation_actor ON ged_index.consultation_log (actor_id, occurred_utc DESC);
-- triggers reject_*_mutation (UPDATE/DELETE) + no_truncate : COPIE EXACTE du pattern document_events (§3.6).
```

Écriture via `IConsultationAuditWriter` (NEUF, `Ged.Contracts`, tenant-scopé). **Robustesse** : par défaut **best-effort + log Warning POUR l'observabilité** (une lecture n'a pas de transaction métier à casser), **MAIS** la motivation diffère de l'audit socle : si une finalité **probante** est confirmée (D8), exiger **fail-closed** (refuser l'accès si la trace échoue) ou au minimum une **alerte de supervision** sur échec (pas un Warning noyé). RGPD/rétention = ❓ NON TRANCHÉ (D8 : minimisation, pseudonymisation `actor_id` après délai).

### 6.7 Portail Blazor (pages vue-pure + handlers, tests obligatoires)

Pages maître dans `Liakont.Host` (page mince + vue-pure bUnit + `IGedQueries` internal via `InternalsVisibleTo`). **Aucune logique métier dans les pages** (handlers MediatR). **Toute page sans test bUnit/Playwright = P1.**

| Écran | Route | Données |
|---|---|---|
| Recherche | `/ged/recherche` | `IDocumentSearchIndex.SearchAsync` (paginée SQL) + facettes |
| Fiche document | `/ged/document/{id}` | `IGedDocumentQueries.GetAsync` + aperçu (`ReadableHtml` du paquet ; `ReadableDocumentRenderer` réservé aux docs fiscaux — cf. RL-16) + **intégrité GED** = re-lecture `IArchiveStore.ReadAsync(_ged/…)` comparée au `content_hash` indexé (§3.4.1) ; `IArchiveVerifier` (chaîne+ancrage) réservé à un `ManagedDocument` **fiscal** (`archive_entry_id` non nul) |
| Exploration objet | `/ged/objet/{entityType}/{id}` | `IGedGraphQueries.ExploreAsync` (traversée bornée §6.4) |

Le « lien coffre » **ouvre/atteste** le paquet, ne le modifie jamais. Montants affichés `decimal` (la fiche **ne recalcule rien**). **Pagination (RL-20)** : la page Recherche n'utilise **pas** le mode chargement-tout de `DeclaredListPage` ; elle consomme des pages déjà bornées côté SQL via `IDocumentSearchIndex.SearchAsync` (keyset) — acceptance : aucun chemin ne matérialise l'intégralité du corpus.

---

## 7. Frontières & impact des règles non négociables

| Règle | Impact GED | Garde à conserver |
|---|---|---|
| **1. `decimal`, jamais float** | Axe `number` polymorphe | `value_number numeric` (decimal C#), échelle portée par `value_scale` de l'axe, arrondi half-up en `ValueNormalizer` ; aucun `double` |
| **2. Aucune règle fiscale inventée** | La GED ne porte aucune règle fiscale | Axes = paramétrage tenant, jamais une source fiscale ; valeur probante tiers = ❓ NON TRANCHÉ |
| **3. Bloquer plutôt qu'affirmer faux** | Mapping & scellement | `DEFER` jamais deviner ; si scellement tiers échoue, ne pas marquer `sealed_at` ; jamais « conforme NF Z42-013 » non vérifié |
| **4. Audit + coffre WORM immuables** | Liens GED, change-logs, `consultation_log`, `ged_index.sealed_refs` (fast-follow) | Triggers append-only (UPDATE/DELETE/TRUNCATE rejetés tout rôle) ; aucune colonne mutable sous append-only (révision par chaînage `supersedes_id`) ; document GED rangé write-once via `IArchiveStore` **hors** chaîne fiscale ; **jamais** d'UPDATE sur `archive_entries` (jamais étendu — option C) |
| **5. Lecture seule base source** | Ingestion GED via agent ODBC RO | Réutilise `AgentBoundaryTests` ; la GED ne réécrit jamais le legacy |
| **6. Frontières / abstractions** | Module `Ged` + 5ᵉ axe | NetArchTest : flux fiscal `NotHaveDependencyOn(Ged.*)` ; `Ged.Infrastructure` ne lit que des `*.Contracts` ; `Archive` ne dépend que de `ISealedArchiveProvider` ; plug-in tiers → `Archive.Contracts`/`Domain` ; agent → `Liakont.Agent.*` (Core pour `IManagedExtractor`, Contracts.Ged pour les DTO), jamais le code plateforme |
| **7. Aucune donnée client dans le code** | Risque max : coder « lot/vente/PV » | **Aucun** axe/entité/relation métier en dur ; seeds en `deployments/<client>/` ; exemples fictifs en `config/exemples/` ; **garde outillée** (§8) |
| **8. Capacités, jamais `if (x is Concret)`** | 5ᵉ axe coffre tiers | `SealedArchiveCapabilities` pilote ; `if (provider is Arkhineo)` interdit (P1) ; aucun flag produit doublonnant une capacité |
| **9. Tenant-scope** | Recherche/liens/graphe | Isolation par connexion ; aucune requête cross-tenant ; exceptions documentées (§3.2) ; **jointure SQL cross-schéma `ged_* → documents.*` interdite** (soft-link logique seulement — garde : **lint/grep des migrations & queries `Ged`**, pas NetArchTest qui ne voit pas le SQL brut) |
| **10. Secrets chiffrés** | Credential coffre tiers par tenant | Chiffré en base par tenant (mécanique des clés PA) ; jamais en clair ni en log |
| **11. Socle vendored intact** | `Ged` et plug-in tiers = code Liakont | Réutilisation socle (`DeclaredListPage`, MediatR, `TenantJobRunner`) sans modification → rien à consigner ; toute modif `Stratum.*` → provenance |
| **12. Messages opérateur en français** | Recherche, échecs scellement, DEFER | Tous messages FR (n° document, action corrective) |
| **Généricité produit** | Risque le plus structurant | Méta-modèle générique ; piège EAV écarté (ADR-0032) ; garde outillée anti-vocabulaire métier (§8) |

Frontières NetArchTest (résumé) :

```
Ged.Web            ──▶ Ged.Contracts
Ged.Infrastructure ──▶ Ged.Domain, Ged.Application, Archive.Contracts, Documents.Contracts,
                       Staging.Contracts   [Contracts only ; l'événement GED vit dans Ged.Contracts, donc PLUS de
                       dépendance Ingestion.Contracts pour l'événement (RL-17) — à ne garder que si l'auth agent partagée l'impose]
Ged.Domain         ──▶ (aucune dépendance fiscale)
Archive            ──▶ ISealedArchiveProvider (abstraction)          ★ jamais SealedProviders.Arkhineo
SealedProviders.*  ──▶ Archive.Contracts + Archive.Domain            ★ jamais Ged, jamais autre module
★ INTERDIT (P1) : Pipeline/Validation/Transmission/Documents ──X──▶ Ged.*   (le flux fiscal ignore la GED)
```

---

## 8. Tests & Definition of Done

| Catégorie | Couverture exigée (acceptance) |
|---|---|
| **NetArchTest** | `NotHaveDependencyOn(Ged.*)` sur Pipeline/Validation/Transmission/Documents ; `Ged.Infrastructure` ne réf. que des `*.Contracts` ; `Archive` ne réf. pas `SealedProviders.*` ; agent → `Liakont.Agent.*` (Core/Contracts.Ged), jamais le code plateforme |
| **Lint SQL cross-schéma** | grep des migrations/queries `Ged` interdisant `documents.`/`mandats.`/`tvamapping.` hors schémas `ged_*` (la jointure cross-schéma est invisible à NetArchTest) |
| **Golden mapping** | `GedMapper` : brut→axes attendus + cas **DEFER** (documentType sans profil, axe requis manquant) |
| **Golden `GedIngestionDecision` (RL-01)** | test golden de la logique RE-COPIÉE : 3 cas (Duplicate/AcceptedAltered/AcceptedNew), ordre doublon-avant-altération, garde hash vide — calqué sur `DocumentIngestionDecisionTests`, pour pinner la non-dérive vs l'original fiscal |
| **Hash-neutralité facture (P1)** | (option C) test : aucune migration ni surface GED ne crée/altère une ligne `documents.archive_entries` ni n'écrit sur le chemin fiscal ; un document GED-seul ne produit **aucune** entrée de chaîne fiscale ; hash d'un paquet **facture** bit-identique après ajout de `IGenericArchiveService`/`IndexAxes` |
| **Isolation cross-tenant** | recherche, facettes, traversée de graphe, liens : document du tenant B invisible depuis tenant A |
| **Append-only** | UPDATE/DELETE/TRUNCATE rejetés sur les 3 tables de liens (`document_axis_links`, `entity_relations`, `document_entity_links`) + 3 change-logs + `consultation_log` (+ `ged_index.sealed_refs` en fast-follow) (intégration base réelle) |
| **Mono-valeur (concurrence, RL-02)** | invariant Domain **sous concurrence** : deux `AppendAxisLinkAsync` simultanés sur un axe mono ⇒ **une seule** valeur courante (advisory lock / `FOR UPDATE`, même tx) ; `current_axis_links` ne renvoie qu'une ligne. **Un test séquentiel est un faux-vert et ne suffit pas.** |
| **Confidentialité** | masquage testé en **lecture, facette, graphe, export, log** |
| **Idempotence `ManagedDocument` + liens (RL-04)** | fast-path post-commit + replay du consommateur ⇒ **un seul** `ManagedDocument` (id du handler, `ON CONFLICT (id) DO NOTHING`) ; les **liens sont écrits par le SEUL consommateur durable**, dans la même tx que `managed_documents.status → 'indexed'` : un replay voit `status='indexed'` et **no-op** (pas de liens dupliqués). Le fast-path ne touche que `managed_documents`. |
| **Rétractation append-only (RL-24)** | retrait `is_retraction` (+ `supersedes_id`) sur `document_axis_links`/`entity_relations`/`document_entity_links` ⇒ l'élément disparaît de sa vue `current_*` (`current_axis_links`/`current_entity_relations`/`current_document_entity_links`) et n'est plus traversé (§6.4), sans UPDATE/DELETE ; testé y compris multi-valeur |
| **Résolution d'identité** | upsert idempotent par `(entity_type, identity_value)` ; **aucune fusion automatique** (geste opéré journalisé, RL-18) |
| **Backfill rétroactif (RL-21)** | chemin DIRECT idempotent (itère `documents.archive_entries`, clé `archive_entry_id`, no-op si déjà indexé) ; documents anciens sans profil → `deferred`, jamais devinés (§10 GED10) |
| **Portail** | bUnit/Playwright sur chaque page ; aucune logique métier en page |
| **Généricité (P1)** | **garde outillée** : un check verify-fast échoue si un littéral `{lot, vente, pv, encheres, adjudication, acheteur, bordereau}` apparaît dans `src/Modules/Ged/**` hors tests/seed ; la démo enchères est intégralement reconstructible depuis `deployments/` sans toucher le code |

**Livrables documentaires** : `MODULE.md` / `INVARIANTS.md` / `SCENARIOS.md` pour le module `Ged` **et** un `MODULE.md` pour chaque plug-in coffre tiers (`SealedProviders.*`). Jeu d'invariants **consolidé** `INV-GED-NNN` : INV-GED-01 anti-EAV ; 02 append-only liens (révision par chaînage) ; 03 mono-valeur Domain ; 04 `attributes` présentation-only ; 05 DEFER jamais deviner ; 06 anti-doublon GED par hash, canal disjoint ; 07 WORM-neutralité (intégrité produit souveraine) ; 08 tenant-scope par connexion ; 09 graphe borné + bidirectionnel ; 10 confidentialité (masquage server-side ; au repos = D9 ouvert) ; 11 `consultation_log` append-only base tenant ; 12 généricité produit (testée).

---

## 9. ADRs à rédiger (allocation unique — résout les collisions de numéros)

> Le repo contient **déjà deux `ADR-0031`** (`-cablage-cycle-run-agent…` et `-licence-fluentassertions…`) : la numérotation libre commence donc à **ADR-0032**. Allocation unique pour TOUTE la GED :

| ADR | Titre | Décision |
|---|---|---|
| **ADR-0032** | Méta-modèle GED (axes + entités polymorphes) | Vrai méta-modèle (axes typés/sourcés/append-only, entités polymorphes), **pas un EAV** ; **un module `Ged`** à trois schémas PG (`ged_catalog`/`ged_index` tenant + `ged_ingestion` système) ; axes/entités = paramétrage tenant |
| **ADR-0033** | Coffre probant tiers / SAE = 5ᵉ axe enfichable (**fast-follow GED20**) | `ISealedArchiveProvider` à capacités, **distinct d'`IArchiveStore`** ; pour une facture l'intégrité produit (hashes + ancrage) reste souveraine, pour un document GED le rangement write-once WORM (option C) ; réf. `ged_index.sealed_refs` append-only |
| **ADR-0034** | Canal d'ingestion générique GED (non-facture) | `IngestedDocumentDto`/`ManagedDocumentReceivedV1` **add-only**, **registre GED dédié** ; jamais l'événement fiscal ; `IManagedExtractor` séparé |
| **ADR-0035** | Recherche & index GED | `tsvector` PG en V1 derrière `IDocumentSearchIndex` ; projection asynchrone reconstructible ; OpenSearch (volume) / pgvector (sémantique) = plug-ins fast-follow |
| **ADR-0036** | Journal de consultation GED append-only | `ged_index.consultation_log` base tenant, triggers WORM ; robustesse best-effort par défaut, fail-closed si finalité probante (D8) |

---

## 10. Plan de construction : lots, items, MVP, V1 vs fast-follow

Segment `ged` (branche `feat/ged`, base `main`), gate humaine `GATE_GED`. **Bande de priorité dédiée 110xx** (la plus haute occupée est ~10390). `depends_on_gate: [GATE_PIPELINE, GATE_CONSOLE_WEB]` (écrans Blazor + harness HTTP) **et** `GATE_AGENT` (les DTO GED vivent dans `Agent.Contracts.Ged`, l'extracteur `IManagedExtractor` dans `Agent.Core`). Archive (CORE_FOUNDATION) est couvert **transitivement** via la chaîne `PIPELINE → PA_FRAMEWORK → CORE_FOUNDATION`. **Séquencement produit (RL-08)** : ajouter `GATE_DEMO_ISATECH` (1ʳᵉ extraction ODBC réelle) aux `depends_on_gate` — **ne PAS seeder le lot GED tant qu'aucun client e-invoicing n'est en production réelle** (la conformité passe d'abord ; cf. D4).

```yaml
# orchestration/manifest.yaml (extrait)
segments:
  ged:
    branch: feat/ged
    base: main
    lots: [GED]                     # fast-follow = items GED2x 'blocked'/'pending' individuels DANS le lot GED (PAS un lot 'GFF' fourre-tout, RL-11)
    depends_on_gate: [GATE_PIPELINE, GATE_CONSOLE_WEB, GATE_AGENT, GATE_DEMO_ISATECH]   # prod réelle d'abord (RL-08)
# Format réel (RL-10) : un item porte 'lot' (rattachement au segment), JAMAIS un champ 'gate' ; une GATE est un
# item dédié {type: gate, executor: human} — une gate non seedée est traitée 'done'.
#   GED00  : { lot: GED, priority: 11000, depends_on: [],                 blueprint: docs-spec-item }  # ADR-0032
#   GED01  : { lot: GED, priority: 11010, depends_on: [GED00],            blueprint: docs-spec-item }  # ADR-0033/34/35/36
#   GED02  : { lot: GED, priority: 11020, depends_on: [GED01] }           # scaffold module + docs + 3 schémas vides
#   GED03a : { lot: GED, priority: 11030, depends_on: [GED02] }           # migrations ged_catalog (entity_types AVANT axis_definitions, RL-07) + Domain catalogue   (split RL-12)
#   GED03b : { lot: GED, priority: 11032, depends_on: [GED03a] }          # ged_index documentaire (managed_documents, axis_links + triggers + vue) + ValueNormalizer (split RL-12)
#   GED03c : { lot: GED, priority: 11034, depends_on: [GED03a] }          # ged_index graphe (entity_instances, entity_relations, document_entity_links + vues current_*) (split RL-12)
#   GED04  : { lot: GED, priority: 11040, depends_on: [GED03b] }          # SetAxisValueCommand + UoW + garde concurrence axe mono (RL-02)
#   GED05a : { lot: GED, priority: 11050, depends_on: [GED02] }           # contrat agent GED (Agent.Contracts.Ged + IManagedExtractor dans Agent.Core, add-only) — dépend GATE_AGENT (split RL-12)
#   GED05b : { lot: GED, priority: 11052, depends_on: [GED03b, GED05a] }  # ingestion plateforme (registre ged_ingestion base système + événement + handler, RL-03) (split RL-12)
#   GED06  : { lot: GED, priority: 11060, depends_on: [GED02] }           # permissions GED dans RolePermissionCatalog
#   GED07  : { lot: GED, priority: 11070, depends_on: [GED05b] }          # IGenericArchiveService + rangement WORM _ged/ (option C)
#   GED08  : { lot: GED, priority: 11080, depends_on: [GED04, GED07] }    # recherche tsvector + facettes + prédicat confidentialité (RL-31)
#   GED09a : { lot: GED, priority: 11090, depends_on: [GED08, GED06], blueprint: blazor-page-item }  # page /ged/recherche  (split portail RL-42)
#   GED09b : { lot: GED, priority: 11092, depends_on: [GED08, GED06], blueprint: blazor-page-item }  # page /ged/document/{id}
#   GED09c : { lot: GED, priority: 11094, depends_on: [GED08, GED06], blueprint: blazor-page-item }  # page /ged/objet/{type}/{id}
#   GED10  : { lot: GED, priority: 11096, depends_on: [GED05b, GED07] }   # backfill rétroactif (chemin DIRECT idempotent, RL-21) + démo enchères/btp seedée
#   GED11  : { lot: GED, priority: 11098, depends_on: [GED03c, GED08] }   # gardes outillées SELF-TESTÉES : lint anti-littéral généricité + lint SQL cross-schéma (RL-27)
#   GATE_GED : { lot: GED, type: gate, executor: human, priority: 11100, depends_on: [GED09a, GED09b, GED09c, GED10, GED11] }  # gate humaine
# Fast-follow (items 'blocked'/'pending' individuels, lot GED, bande 115xx — RL-11) :
#   GED20 : { lot: GED, priority: 11500, depends_on: [GED07], status: blocked }  # coffre tiers Arkhineo/SAE (ISealedArchiveProvider + sealed_refs) — BLOCKED sur D1
#   GED21 : { lot: GED, priority: 11510, depends_on: [GED08] }                   # OpenSearch (hors-gate)
#   GED22 : { lot: GED, priority: 11520, depends_on: [GED08] }                   # pgvector sémantique (hors-gate)
#   GED23 : { lot: GED, priority: 11530, depends_on: [GED05b] }                  # OCR (hors-gate)
#   GED24 : { lot: GED, priority: 11540, depends_on: [GED03c] }                  # relations objet-à-objet avancées (hors-gate)
```

```yaml
# orchestration/items/GED.yaml (items représentatifs ; GED03/GED05 montrés ici pré-split pour la lisibilité — le split RL-12 est dans la liste ci-dessus)
items:
  GED02:
    title: "Scaffold module Liakont.Modules.Ged (5 couches + MODULE/INVARIANTS/SCENARIOS + schémas ged_catalog/ged_index (tenant) + ged_ingestion (système) vides)"
    description: |
      Créer src/Modules/Ged (Contracts/Domain/Application/Infrastructure/Web). MODULE.md/INVARIANTS.md/
      SCENARIOS.md obligatoires. Trois schémas PG : ged_catalog + ged_index (base tenant) + ged_ingestion (base système, RL-03) créés vides par migration. AUCUNE
      table métier ici (séparé pour ne pas surdimensionner — précédents OPS03/API01/SUP01). NetArchTest :
      Ged n'accède aux autres modules QUE par Contracts.
    acceptance:
      - "verify-fast .NET 10 vert ; module compile ; NetArchTest Ged green."
      - "MODULE.md/INVARIANTS.md/SCENARIOS.md présents ; jeu INV-GED-NNN consolidé listé."
      - "Schémas ged_catalog + ged_index (tenant) + ged_ingestion (système) créés (migration), aucune table métier."

  GED03:
    title: "Migrations méta-modèle (axes/entités/liens) + triggers append-only + Domain polymorphe"
    description: |
      Migrations DbUp V### (ordre FK, RL-07 : entity_types AVANT axis_definitions) : entity_types, axis_definitions, axis_values, entity_instances, entity_relations,
      document_axis_links (append PUR via supersedes_id + vue current_axis_links), document_entity_links,
      managed_documents (+ change_log), catalog_change_log, managed_document_change_log, entity_instance_change_log.
      Triggers append-only (UPDATE/DELETE/TRUNCATE rejetés tout rôle, calqués reject_archive_entry_mutation) sur
      les 3 tables de liens (document_axis_links, entity_relations, document_entity_links) + 3 change-logs. Soft-link sans FK vers documents.documents (OPTIONNEL : un doc GED
      métier n'a pas de pendant fiscal). Domain pur : EntityType polymorphe (pas d'enum figé), ValueNormalizer
      (échelle par axe, decimal half-up), validation valeur d'axe → BLOQUER si non conforme, jamais deviner.
    acceptance:
      - "Test base réelle : UPDATE/DELETE/TRUNCATE rejeté sur les 3 liens + 3 change-logs."
      - "Test CONCURRENT : deux AppendAxisLinkAsync simultanés sur un axe mono ⇒ une seule valeur courante (advisory lock / FOR UPDATE, RL-02) ; un test séquentiel ne suffit pas."
      - "Migrations sur base vierge dans l'ordre DbUp : FK axis_definitions→entity_types satisfaite (RL-07)."
      - "Aucun axe/entité 'lot/vente/PV' en dur ; enchères = seed deployments/ ; check anti-littéral vert."
      - "value_number = decimal (jamais double), échelle portée par l'axe ; run-tests vert."

  GED05:
    title: "Ingestion générique GED (DTO add-only + registre GED dédié en base système + événement ManagedDocumentReceivedV1)"
    description: |
      IngestedDocumentDto + ManagedExtractorCapabilitiesDto dans Liakont.Agent.Contracts.Ged (DTO purs) ; IManagedExtractor dans Liakont.Agent.Core (comme IExtractor, RL-15)
      (netstandard2.0, add-only, hash pivot facture INCHANGÉ). Endpoint POST /api/agent/v1/managed-documents/batch.
      Handler : GedCanonicalJson + PayloadHasher (réutilise primitive) -> GedIngestionDecision.Evaluate (logique pure
      RE-COPIÉE dans Ged.Domain, PAS Ingestion.Domain ; RL-01) -> en BASE SYSTÈME (atomique avec l'outbox, RL-03),
      INSERT ged_ingestion.ged_received_documents (channel GED dédié, JAMAIS ingestion.received_documents) +
      ManagedDocumentReceivedV1 (outbox, JAMAIS DocumentReceivedV1) + staging (module Staging) ; PAS d'appel à
      IDocumentIntake (aucun Document fiscal créé).
    acceptance:
      - "Un document non-facture est ingéré (registre GED + événement GED) ; réingestion idempotente (Duplicate)."
      - "Registre GED + ManagedDocumentReceivedV1 écrits ATOMIQUEMENT en base système ; l'événement est réellement drainé (OutboxWorker) et le consommateur crée le ManagedDocument (RL-03)."
      - "Aucune ligne dans documents.documents ; aucun passage par DocumentReceivedConsumer ; aucun état fiscal atteint."
      - "Agent.Contracts add-only : hash du pivot facture inchangé (test golden)."
      - "NetArchTest : agent → Liakont.Agent.* (Core/Contracts.Ged), jamais le code plateforme ; aucune logique métier côté agent."

  GED07:
    title: "Surface d'archivage générique IGenericArchiveService + adaptateur de rangement coffre WORM"
    description: |
      Archive.Contracts : IGenericArchiveService.ArchiveManagedDocumentAsync(GedArchivePackageRequest) +
      AddManagedAddendumAsync — additif, hash-neutre pour la facture (ArchiveIssuedDocumentAsync intact). IndexAxes
      = projection PLATE ArchiveIndexAxis (PAS DocumentAxisLink du module GED). Arborescence _ged/{kind}/{année}/
      {mois}/{clé}/ ; FiscalControlExportService exclut _ged/ d'un export de CONTRÔLE fiscal. Le module Ged appelle
      cette surface ; son implémentation range les octets write-once via IArchiveStore sous _ged/… et N'INSÈRE
      RIEN dans la chaîne fiscale archive_entries (option C, RL-05). Le port coffre tiers ISealedArchiveProvider
      et sa table sealed_refs ne sont PAS posés en V1 : ils arrivent avec GED20 (premier provider).
    acceptance:
      - "Un document GED est rangé write-once dans le coffre du tenant via Archive.Contracts ; re-rangement idempotent."
      - "Aucune ligne documents.archive_entries créée pour un document GED-seul (option C) ; le coffre fiscal n'est pas touché."
      - "Hash d'un paquet FACTURE inchangé (test golden) ; _ged/ exclu de l'export de contrôle fiscal."
      - "NetArchTest : Ged → Archive.Contracts seulement (jamais Archive.Domain/store concret) ; Archive.Contracts ne réf. pas DocumentAxisLink."
```

### MVP (tranche verticale) — preuve de valeur sans coffre tiers

Intersection GED02→GED11 (hors fast-follow GED20-24) : *« j'ingère un document métier quelconque (non-facture), je l'indexe sur 2-3 axes + 1 entité, je le range dans le coffre WORM existant, et je le RETROUVE par recherche multidimensionnelle — générique, tenant-scopé, sans toucher le flux fiscal »*. **Démo enchères** : seed `deployments/<demo-encheres>/` (axes `numero_lot`/`numero_vente`/`acheteur`, type `acheteur`) → indexer un **bordereau acheteur** → filtrer par `numero_lot` → tous les documents du lot remontent. **2ᵉ métier (situation de travaux)** : seed `deployments/<demo-btp>/` (axes `numero_situation`/`mois`/`montant_ht_cumule`(EUR, scale=2)/`avancement_pct`(scale=0), type `chantier`) → le **même** `document_axis_links` porte `montant_ht_cumule` (EUR) et `avancement_pct` (%) sans un seul `ALTER TABLE` : généricité **prouvée par la configuration**.

### V1 vs FAST-FOLLOW

| Capacité | V1 (`GATE_GED`) | Fast-follow (items GED20-24, distincts) |
|---|---|---|
| Coffre de rangement | WORM Liakont via `IGenericArchiveService` | **GED20** coffre tiers Arkhineo/SAE (`ISealedArchiveProvider`) — **blocked sur D1** |
| Recherche | PG `tsvector` + facettes (`IDocumentSearchIndex`) | **GED21** OpenSearch / **GED22** pgvector |
| Contenu | métadonnées + couche texte fournie | **GED23** OCR |
| Relations | graphe générique borné bidirectionnel | **GED24** relations objet-à-objet avancées |

Chaque fast-follow entre par une **abstraction à capacités** : aucun `if (provider is Arkhineo)`, aucun `if (index is OpenSearch)` (P1). **Mais l'abstraction coffre-tiers `ISealedArchiveProvider` n'est PAS posée en V1** : elle arrive **avec** GED20 et son premier provider (pas de code dormant, RL-26 + option C). En V1, les seules abstractions à capacités réellement livrées sont `IDocumentSearchIndex` (recherche) et `IGenericArchiveService` (rangement WORM hors chaîne fiscale).

---

## 11. Décisions ouvertes

> **Tranché (redline RL-05) — approche d'archivage GED = option C** : un document GED-seul est rangé **write-once WORM via `IArchiveStore`** (espace `_ged/…`), **hors de la chaîne de hashes fiscale `archive_entries`** et sans ancrage RFC 3161 en V1 ; le coffre fiscal n'est **jamais** touché. La valeur probante renforcée (coffre tiers / NF Z42-013 / chaîne GED dédiée) est **déférée au fast-follow GED20**. Cf. §5.1 et INV-ARCH-GED-1. Les **4 autres P1** du redline sont désormais **traités dans le corps de la spec** : RL-01 frontière `DocumentIngestionDecision` (§4.3, `GedIngestionDecision` dans `Ged.Domain`), RL-02 concurrence mono-valeur (§3.4.3/§3.7, garde advisory lock + test concurrent), RL-03 registre/outbox en base système (§2.1/§3.2(a)/§4.3.1, schéma `ged_ingestion`), RL-07 ordre des migrations (§3.3.1/§5.4). **Restent ouverts** uniquement les arbitrages humains **D1–D3 et D6–D12** ci-dessous (**D4 et D5 tranchés par Karl le 2026-06-25**), et la **vague P2** du redline (frontières/append-only/recherche, à intégrer avant le seeding du lot).

| # | Décision | Owner | Reco |
|---|---|---|---|
| D1 | Valeur **probante** d'une attestation tierce (Arkhineo / NF Z42-013) et format de **vérification autonome** | EC + juridique | ❓ NON TRANCHÉ — tant que non spécifié : statut `SealClaimedNotVerifiable` ; jamais « conforme NF Z42-013 » ; ceinture+bretelles (ancrage produit toujours actif) |
| D2 | Liakont peut-il **revendiquer** NF Z42-013 via un SAE tiers, ou seulement « scellé chez un tiers certifié » ? | Juridique | ❓ NON TRANCHÉ — ne revendiquer que ce que le fournisseur atteste |
| D3 | **Rétention/cycle de vie d'un document GED NON fiscal** vs WORM inaltérable ↔ droit à l'effacement RGPD (art. 17) | DPO + juridique | ❓ NON TRANCHÉ — `retention_class` par document (legal_hold/tenant_bounded/erasable) ; le **crypto-shredding** (effacer la clé de chiffrement du contenu) **suppose une couche de chiffrement-au-repos du contenu qui N'EXISTE PAS aujourd'hui** (`IArchiveStore` écrit des octets BRUTS — RL-06) → c'est un **prérequis (D9)**, pas un mécanisme disponible en V1 ; rangement WORM **conditionnel** au régime probant ; par défaut sûr = pas de purge auto |
| D4 | **Périmètre V1** : MVP strict (coffre Liakont seul) ou coffre tiers dès V1 ? | Karl | ✅ **TRANCHÉ (Karl, 2026-06-25) — MVP strict V1** : coffre WORM Liakont seul (option C) ; coffre tiers Arkhineo/SAE = fast-follow GED20 (dépend de D1) |
| D5 | **Pricing / positionnement** (upsell facturé séparément ? palier par tenant/volume/axe ?) | Karl | ✅ **TRANCHÉ (Karl, 2026-06-25)** — **upsell facturé séparément** ; activation = **capacité tenant en base** (jamais couplée au code, règle 8) ; **palier simple « on/off par tenant » en V1** (palier volume/axe reporté à D10) ; coffre tiers = option premium GED20 |
| D6 | Référence `ged_index → ged_catalog` : FK intra-base **ou** validation applicative | Archi | Validation applicative (symétrie inter-modules ; cohérent `reconciliation_queue`) |
| D7 | **Découpage** : un module `Ged` **ou** split `Ged.Catalog`/`Ged.Search` | Archi | UN module `Ged` (3 schémas PG : `ged_catalog`/`ged_index` tenant + `ged_ingestion` système) en V1 ; split si montée en charge recherche prouvée |
| D8 | `consultation_log` : best-effort **ou** fail-closed si finalité probante ; rétention/minimisation RGPD | Sécurité + DPO | Best-effort par défaut ; fail-closed + alerte si probant ; minimiser, pseudonymiser après délai |
| D9 | **Chiffrement au repos** des valeurs d'axes `is_confidential` | Sécurité | ❓ NON TRANCHÉ — poser explicitement (cohérent règle 10) ; à arbitrer selon sensibilité |
| D10 | Volumétrie cible par tenant & **seuil chiffré** de bascule `tsvector → OpenSearch` ; synchronisation index synchrone vs asynchrone | Produit + exploitation | Projection **asynchrone** (latence d'ingestion découplée) ; fixer une volumétrie de référence pour rendre « au volume » vérifiable |
| D11 | **Multilingue** du contenu indexé (config `tsvector` ≠ `french`) | Produit | V1 `french` (FR-only aligné F10) ; contenu non-FR best-effort ; multilingue fast-follow |
| D12 | Backfill **rétroactif** du corpus déjà scellé (item **GED10**, séparé du portail GED09a/b/c — RL-42) : chemin DIRECT hors-outbox | Archi | Job tenant-scopé itérant `documents.archive_entries`, clé = `archive_entry_id`, **no-op si déjà indexé** ; aperçu/texte via `ReadableHtml` du paquet (`ReadableDocumentRenderer` réservé aux factures, RL-16) ; `deferred` si pas de profil ; **geste opéré**, idempotent, pas un effet de bord du flux (RL-21) |

---

*Fichiers de référence (existants, non modifiés par cette conception)* : `Archive/Contracts/IArchiveService.cs`, `ArchivePackageRequest.cs`, `ArchiveAttachment.cs`, `Archive/Domain/IArchiveStore.cs`, `ArchiveStoreCapabilities.cs`, `ITimestampAnchor.cs`, `HashChain.cs`, `PackageHasher.cs` ; `Documents/Infrastructure/Migrations/V005__create_archive_entries_table.sql` (pattern WORM copié) ; `Ingestion/Infrastructure/Migrations/V004__create_received_documents_table.sql` (registre — non partagé, cf. §4.3.1) ; `Reconciliation/Infrastructure/Migrations/V002__create_reconciliation_queue_table.sql` (soft-link sans FK) ; `TvaMapping` (`MappingTable`/`MappingRule`/`MappingChangeLog`) ; `Host/Liakont.Host/Security/RolePermissionCatalog.cs` (matrice à amender, §6.5).