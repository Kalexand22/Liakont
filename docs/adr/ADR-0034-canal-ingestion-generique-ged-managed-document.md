# ADR-0034 — Canal d'ingestion générique GED par agents : `IngestedDocumentDto` / `ManagedDocumentReceivedV1` add-only, registre dédié en base système, `IManagedExtractor` distinct

- **Statut** : Proposé (2026-06-25).
- **Date** : 2026-06-25
- **Nature** : cet ADR **précède** le chantier d'implémentation (module `Liakont.Modules.Ged` non démarré,
  **aucun code**). Les sections **Décision** et **Invariants** sont **normatives** : elles décrivent la **cible**, pas
  l'état du code. Aucun invariant n'est garanti tant qu'il n'est pas livré **et** prouvé par test. Cet ADR **dérive de**
  la conception `docs/conception/F19-GED-Dynamique-Coffre-Fort.md` (statut « proposition NON RATIFIÉE ») et n'invente
  **aucune** règle fiscale, légale ou probante (CLAUDE.md n°2). Il est une **sœur d'ADR-0032/0033/0035/0036** : il tranche
  une **frontière de comportement** (comment un document métier **arbitraire** entre dans la GED) et les **réutilisations
  exactes** de l'existant fiscal qu'elle exige ; il ne tranche **aucun point fiscal** et ne touche **aucune** surface du
  flux e-invoicing. Il consomme le méta-modèle d'ADR-0032 (qui crée `ManagedDocument` et les liens) et alimente
  ADR-0033 (archivage) et ADR-0035 (index de recherche).
- **Numérotation** : ADR-**0034**. La numérotation libre de la GED (F19 §9) commence à **0032** (le repo contient déjà
  DEUX `ADR-0031` — `-cablage-cycle-run-agent…` et `-licence-fluentassertions…`). Plan d'ADR GED : **0032** méta-modèle,
  **0033** coffre tiers/option C (fast-follow GED20), **0034** ingestion générique, **0035** recherche & index, **0036**
  journal de consultation.
- **Contexte décisionnel** : `docs/conception/F19-GED-Dynamique-Coffre-Fort.md` §2.4 (flux MVP de bout en bout, deux
  canaux), §3.2 (db-per-tenant et exceptions documentées, registre en base système), **§4 complet** (coexistence des
  deux canaux, `IngestedDocumentDto`, réutilisation exacte, registre GED dédié, contenu, résolution d'identité, mapping
  déclaratif, côté agent), §8 (golden `GedIngestionDecision` RL-01, idempotence RL-04, hash-neutralité facture,
  goldens de mapping) ; sources socle/code réelles citées par F19 :
  `src/Modules/Ingestion/Infrastructure/Migrations/V004__create_received_documents_table.sql` (unique `(tenant_id, payload_hash)`,
  `contract_version text NOT NULL`), `PostgresReceivedDocumentUnitOfWork` (registre + outbox dans la **même**
  transaction, base système), `CanonicalJsonWriter` (ordre figé, null omis, ASCII, enums par nom),
  `PayloadHasher.ComputeHash(string)` (SHA-256 des octets), `DocumentIngestionDecision.Evaluate`
  (`Ingestion.Domain`, 3 cas), `IDocumentIntake`, `IIngestedPdfStore`, module `Staging` (`Staging.Contracts`) ;
  ADR liés : **ADR-0032 — Méta-modèle GED dynamique : axes typés et entités polymorphes append-only (anti-EAV),
  module unique `Liakont.Modules.Ged` à trois schémas PostgreSQL**, **ADR-0033 — Coffre probant tiers / SAE comme 5ᵉ
  axe enfichable (`ISealedArchiveProvider`) et archivage WORM des documents GED hors chaîne fiscale (option C ;
  fast-follow GED20)**, **ADR-0035 — Recherche & index GED : `tsvector` PostgreSQL derrière `IDocumentSearchIndex`,
  projection asynchrone reconstructible, graphe borné bidirectionnel**, **ADR-0036 — Journal de consultation GED
  append-only (`ged_index.consultation_log`, base tenant, WORM) : best-effort par défaut, fail-closed si finalité
  probante** ; ADR socle : `docs/adr/ADR-0007-serialisation-canonique-pivot.md`,
  `docs/adr/ADR-0014-staging-durable-contenu-pivot-intake.md`, `docs/adr/ADR-0006-mecanique-jobs-multi-tenant.md`,
  `docs/adr/ADR-0016-job-tenant-scope.md`.

## Contexte

La GED Liakont doit **ingérer des documents métier arbitraires** (PV, contrats, courriers, bordereaux, situations de
travaux), pas seulement des factures EN 16931 (F19 §1.1). Or le canal d'ingestion existant est **entièrement façonné
pour la facture** : `PivotDocumentDto` est **fermé, aligné EN 16931, hashé canoniquement** ; le registre
`ingestion.received_documents` a un index `(tenant_id, payload_hash)` **sans discriminant de canal** ; et tout document
accepté traverse `IDocumentIntake` pour devenir un `Document` fiscal `Detected` qui déclenche le pipeline d'émission.
Aucune de ces trois briques ne peut servir telle quelle au document GED sans corrompre la conformité fiscale.

Le piège central que cette décision évite est **la contamination du canal fiscal**. Trois contaminations concrètes
sont en jeu. (1) **Hash** : surcharger `PivotDocumentDto` d'un champ GED casserait la stabilité octet du hash fiscal
(ADR-0007) — un faux `AcceptedAltered` sur une facture inchangée serait une régression silencieuse. (2) **Registre** :
partager `ingestion.received_documents` ferait cohabiter deux sérialiseurs canoniques disjoints dans le **même** espace
de hash `(tenant, hash)` → un document GED dont le hash collisionne avec une facture produirait un faux `Duplicate`
côté fiscal (F19 §4.3.1). (3) **Cycle de vie** : appeler `IDocumentIntake` créerait un `Document` fiscal pour un
document qui n'a aucune contrepartie fiscale, l'injectant à tort dans le pipeline d'émission. Le découplage doit donc
être **structurel**, pas seulement « prouvé par test » : deux DTO disjoints, deux endpoints, un registre propre en base
système, un événement propre, et **un seul moteur** dont les primitives déterministes (`PayloadHasher`, la canonisation
JSON, la décision d'ingestion) sont **réutilisées sans être partagées** par-dessus une frontière de module.

La seconde force en présence est la **frontière de généricité** (CLAUDE.md n°6 ; blueprint §2/§6). L'agent **n'a aucune
logique métier** : il extrait des champs **bruts** et déclare une forme ; **toute interprétation vit sur la plateforme**.
Le mapping d'un document brut vers des axes/entités/relations est une **généralisation du mapping TVA** déjà en
production (`MappingTable`/`MappingRule`/`MappingChangeLog`), avec un vocabulaire figé : un type inconnu ou un axe
requis non résolu **DEFER** (range en `deferred`, visible console), **jamais** ne mappe au hasard ni ne rejette en
silence. Là où le mapping fiscal **bloque** (enjeu fiscal), le mapping GED **diffère** (enjeu d'indexation, pas de
conformité) — le coffre reste alimentable, l'indexation peut suivre.

La troisième force est l'**add-only strict** (ADR-0007 §extension de contrat). Le contrat agent fiscal
(`IExtractor`, `PivotDocumentDto`, `ExtractorCapabilitiesDto`) ne doit **subir aucune mutation** : tout est **ajouté à
côté** (`Liakont.Agent.Contracts.Ged`, `IManagedExtractor`), pour que le hash du pivot facture reste **bit-identique**
(test golden) et que les invariants extracteur facture R1–R9 soient préservés.

## Décision

### 1. DEUX canaux DISJOINTS, UN seul moteur (F19 §4.1)

`PivotDocumentDto` est **fermé, aligné EN 16931, hashé canoniquement** ; le surcharger casserait la stabilité octet du
hash fiscal (ADR-0007). On ne le touche pas. On pose **deux DTO disjoints, deux endpoints, un moteur partagé** :

| | Canal fiscal (**inchangé**) | Canal GED (**NEUF**) |
|---|---|---|
| DTO | `PivotDocumentDto` | `IngestedDocumentDto` (`Liakont.Agent.Contracts.Ged`) |
| Endpoint | `POST /api/agent/v1/documents/batch` | `POST /api/agent/v1/managed-documents/batch` (≤ 100, `ManagedExtractorCapabilities`) |
| Sortie | `Document` `Detected` → pipeline fiscal | `ManagedDocument` + liens — **JAMAIS** un `Document` fiscal |
| Événement | `DocumentReceivedV1` | `ManagedDocumentReceivedV1` (`ged.managed-document.received`) |
| Hash | `PayloadHasher` sur JSON canonique pivot | `PayloadHasher` (**même primitive**) sur `GedCanonicalJson` |

La sortie du canal GED est un `ManagedDocument` (entité d'ADR-0032), **jamais** un `Document` du module `Documents`.
Le handler GED **n'appelle JAMAIS** `IDocumentIntake` et **n'écrit JAMAIS** `DocumentReceivedV1`. Le pont « une facture
fiscale doit aussi apparaître en GED » est un **consommateur dédié** qui crée un `ManagedDocument` soft-linké au
`documents.documents` ; ce **n'est JAMAIS** un abonnement du module `Ged` à `DocumentReceivedV1` (qui déclenche le
pipeline d'émission — F19 §2.4 NB, §6.1).

### 2. `IngestedDocumentDto` (NEUF) — DTO PUR, agent sans logique métier (F19 §4.2, §4.6)

DTO **pur** dans `Liakont.Agent.Contracts.Ged` (**netstandard2.0**). L'agent **extrait brut et déclare** ; **AUCUNE
interprétation côté agent** (CLAUDE.md n°6) — classification, mapping, normalisation numérique vivent **sur la
plateforme**. Symétrie pivot « champ absent → `null` ».

```csharp
namespace Liakont.Agent.Contracts.Ged;   // NEUF

public sealed class IngestedDocumentDto
{
    string SourceReference;                 // clé de réconciliation + altération (obligatoire)
    string DocumentType;                    // type dans la source, valeur BRUTE (jamais classé par l'agent)
    DateTime? SourceTimestampUtc;
    IngestedContentRef? Content;            // { ContentRef, MediaType, ByteLength, ContentHash } ; null si pas de binaire
    IReadOnlyDictionary<string,string> SourceFields;   // BRUT ; ÉMIS TRIÉ PAR CLÉ (ordinal) par GedCanonicalJson
                                                       // sinon l'anti-doublon (tenant,hash) casse (RL-39). PAS un EAV plateforme
    IReadOnlyList<RawAxisHint> SourceAxes;       // { Path/Name, Values[] }
    IReadOnlyList<RawEntityHint> SourceEntities; // { Type, ExternalId, Display }
    IReadOnlyList<RawRelationHint> SourceRelations; // { Type, TargetExternalId, TargetType }
}
```

⚠️ **`SourceFields` est un dictionnaire** : son ordre d'énumération n'est pas déterministe à travers les runtimes
(net48 agent vs .NET 10 plateforme). Il **DOIT être ÉMIS TRIÉ PAR CLÉ (comparaison ordinal)** par `GedCanonicalJson` —
**sinon** deux sérialisations du même contenu diffèrent octet-à-octet, le hash diffère, et l'anti-doublon `(tenant, hash)`
**casse** (RL-39). `SourceFields` reste **BRUT**, et **n'est PAS** un EAV plateforme : seul un **axe déclaré** (ADR-0032)
est interrogeable ; ces champs ne sont qu'une matière d'entrée pour le mapping.

`ManagedExtractorCapabilitiesDto` (NEUF, **disjoint** d'`ExtractorCapabilitiesDto`, DTO pur dans
`Liakont.Agent.Contracts.Ged`) : `ProvidesManagedDocuments`, `ProvidesAxes`, `ProvidesEntities`, `ProvidesRelations`,
`ProvidesBinaryContent`. **`IManagedExtractor` vit dans `Liakont.Agent.Core`** — comme `IExtractor`, **PAS** dans
`Agent.Contracts` (RL-15) — et est **séparé d'`IExtractor`** pour préserver les invariants extracteur facture R1–R9 :
`SourceName`, `Capabilities`, `IEnumerable<IngestedDocumentDto> ExtractManagedDocuments(from, to)` (streaming, lecture
seule, idempotent), `Stream OpenContent(sourceReference)`. Un adaptateur peut implémenter l'un, l'autre, ou les deux.

**Add-only strict (ADR-0007)** : aucun membre existant n'est touché ; le hash du **pivot facture est inchangé** (test
golden) ; aucun invariant extracteur facture n'est modifié.

### 3. Réutilisation EXACTE de l'existant + frontière de module (F19 §4.3)

| Brique existante | Réutilisation GED | Frontière |
|---|---|---|
| `PayloadHasher.ComputeHash(string)` | **réutilisé TEL QUEL** (SHA-256 des octets ; ne porte AUCUN déterminisme) | primitive partagée (`Liakont.Agent.Contracts.Serialization`, contrat agent↔plateforme) |
| `CanonicalJsonWriter` (ordre figé, null omis, ASCII, enums par nom) | **NEUF `GedCanonicalJson.Serialize(IngestedDocumentDto)` bâti SUR `CanonicalJsonWriter`** + `SourceFields` trié par clé (ordinal) | golden **cross-runtime net48/.NET 10** (RL-39) |
| `DocumentIngestionDecision.Evaluate` (logique pure, `Ingestion.Domain`, 3 cas) | **RE-COPIÉE** dans `Ged.Domain` comme `GedIngestionDecision` (record struct, 3 cas : `Duplicate` / `AcceptedAltered` / `AcceptedNew`) | **PAS** une référence à `Ingestion.Domain` (frontière, RL-01) |

Le **déterminisme du hash** n'est pas dans `PayloadHasher` (qui hache des octets bruts) mais dans la **canonisation** :
`GedCanonicalJson` est bâti **sur** `CanonicalJsonWriter` (mêmes garanties — ordre des membres figé, propriétés nulles
omises, échappement ASCII, enums sérialisées par nom), **plus** le tri `SourceFields` par clé (ordinal, §2). Un **golden
cross-runtime** (le même `IngestedDocumentDto` sérialisé en net48 et en .NET 10 produit des octets identiques) est
exigé, sinon l'anti-doublon est faux-vert (RL-39).

La logique de décision d'ingestion (« déjà vu ce hash → `Duplicate` ; même `source_reference` mais hash différent →
`AcceptedAltered` ; jamais vu → `AcceptedNew` », avec **ordre doublon-avant-altération** et **garde hash vide**) est
**RE-COPIÉE** dans `Ged.Domain` (`GedIngestionDecision`), **pas référencée** depuis `Ingestion.Domain` : un module
n'accède au Domain d'un autre module **jamais** (CLAUDE.md n°6, RL-01 ; NetArchTest). Un **golden** calqué sur
`DocumentIngestionDecisionTests` (3 cas, ordre, garde hash vide) **pinne la non-dérive** vis-à-vis de l'original fiscal :
si l'un évolue sans l'autre, le golden rouge le signale. (Option (b) F19 : une primitive neutre en `Common/Abstractions`
par ADR séparé — **non retenue ici** ; la re-copie + golden est le défaut défendable.)

### 4. Registre GED DÉDIÉ en BASE SYSTÈME (F19 §3.2(a), §4.3.1 ; RL-03)

Le partage de `ingestion.received_documents` est **écarté** : son unique index `(tenant_id, payload_hash)` n'a **aucun
discriminant de canal**, et deux sérialiseurs canoniques disjoints y créeraient de faux `Duplicate` / `AcceptedAltered`
sur le canal fiscal. On crée un **registre GED propre, en BASE SYSTÈME** (schéma `ged_ingestion`), **co-localisé avec
l'outbox** — exactement comme `ingestion.received_documents` vit en base système (`PostgresReceivedDocumentUnitOfWork`,
`V004`) — pour que **l'INSERT du registre ET l'écriture de `ManagedDocumentReceivedV1` soient ATOMIQUES dans une seule
transaction** (il n'existe **pas** de 2PC entre deux bases PostgreSQL ; RL-03) :

```sql
CREATE TABLE IF NOT EXISTS ged_ingestion.ged_received_documents (
    id                  uuid        NOT NULL DEFAULT gen_random_uuid(),
    tenant_id           text        NOT NULL,                  -- BASE SYSTÈME (schéma ged_ingestion), co-localisé outbox
    source_reference    text        NOT NULL,
    payload_hash        text        NOT NULL,
    managed_document_id uuid        NOT NULL,
    contract_version    text        NOT NULL,                  -- version du contrat Liakont.Agent.Contracts.Ged (jamais une version fiscale)
    received_at         timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_ged_received_documents PRIMARY KEY (id)
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_ged_received_tenant_payload ON ged_ingestion.ged_received_documents (tenant_id, payload_hash);
CREATE INDEX IF NOT EXISTS ix_ged_received_tenant_source ON ged_ingestion.ged_received_documents (tenant_id, source_reference, received_at DESC);
```

Cette table est une **exception db-per-tenant documentée** (F19 §3.2(a)) : par défaut la GED n'a aucune colonne
`tenant_id` (l'isolation **EST** la connexion), mais le **registre d'ingestion** vit en base système — la seule façon
d'écrire **atomiquement** registre + événement — et porte donc `tenant_id` (résolu par slug à l'ingestion). L'index GED
de la **base tenant** (`ged_catalog`/`ged_index`) est peuplé **en aval** par le consommateur de l'événement (RL-03).

**On N'APPELLE PAS `IDocumentIntake`** (aucun `Document` fiscal créé) ; **on NE PARTAGE PAS `ingestion.received_documents`**
(isolation totale des espaces de hash ; l'anti-doublon `(tenant, hash)` GED est strictement local). Le pattern UoW est
**calqué sur `PostgresReceivedDocumentUnitOfWork`** (registre + outbox dans la même transaction). Le canal GED versionne
son **propre** contrat (`Liakont.Agent.Contracts.Ged`) dans `contract_version`, jamais une version fiscale (F19 §4.3.3).

### 5. Ordre d'écriture (invariant ADR-0014) : staging AVANT la transaction ; contenu via un store SÉPARÉ (F19 §2.4, §4.3.2)

Le contenu pivot GED est écrit **`staging.WriteAsync(canonicalJson)` AVANT la transaction** (dépendance explicite au
**module `Liakont.Modules.Staging`**, `Staging.Contracts`, **pas** au module Ingestion), sous le **`StagedPayloadKey`
obligatoire** (`DocumentId` non vide, RL-38) : `new StagedPayloadKey(tenantId, managed_document_id, gedPayloadHash)`. Cet
ordre respecte l'**invariant de staging durable d'ADR-0014** : le blob est posé avant le commit PG ; ce **n'est PAS
atomique** (il n'existe pas de 2PC blob↔PG), exactement comme le canal fiscal — un blob orphelin sans ligne committée est
inerte et toléré.

Le **binaire de contenu GED** transite par un **`IIngestedContentStore` SÉPARÉ** (`SaveContentAsync` / `OpenContentAsync`
/ `ExistsAsync`), avec **anti path-traversal conservé** (`SafeTenant` + nom de fichier nu + contrôle sous-racine). On
**ne fond PAS** `IIngestedPdfStore` (concern **fiscal**) : il **reste intact**. Sa sémantique est **DUALE** : le **buffer**
de réconciliation (`SavePooledPdfAsync` = `CreateNew`) **N'ÉCRASE PAS** (un PDF déjà en pool n'est pas réécrit, RL-40) ;
c'est le **chemin lié** (PDF attaché à un document) qui **écrase** (RL-40). Le buffer agent peut être écrasable ; le binaire
**probant** GED est write-once **au coffre via Archive** (ADR-0033, hors chaîne fiscale, option C), pas dans ce store de
transit.

### 6. Idempotence (F19 §2.4, §3.4.1, §8 ; RL-04)

L'**id du `ManagedDocument` est attribué par le handler d'ingestion** et **porté dans `ManagedDocumentReceivedV1`** ;
c'est l'ancre d'idempotence de bout en bout.

1. **Fast-path post-commit, best-effort** : après le commit (registre + événement), le handler peut créer **uniquement**
   la ligne `managed_documents` (`INSERT … ON CONFLICT (id) DO NOTHING`). Il **ne crée AUCUN lien**. C'est une
   optimisation de latence, pas le chemin durable : s'il échoue, l'événement durable garantit le rattrapage.
2. **Les LIENS sont écrits par le SEUL consommateur durable** de `ManagedDocumentReceivedV1` (module `Ged`), **dans la
   même transaction** que la transition `managed_documents.status → 'indexed'`. Un **replay** voit `status = 'indexed'`
   et **no-op** (pas de liens dupliqués). Le consommateur upsert le `ManagedDocument` (id du handler,
   `ON CONFLICT (id) DO NOTHING`), écrit les liens, puis pose `status='indexed'` — **une seule transaction**.
   **GARDE DE CONCURRENCE OBLIGATOIRE (RL-04)** : l'`OutboxWorker` est **at-least-once** et **dispatche AVANT** de
   marquer l'événement traité (pas de `FOR UPDATE SKIP LOCKED`) → **deux livraisons concurrentes** lisent toutes deux
   `status='draft'` et écrivent toutes deux les liens. Comme la PK des liens est `gen_random_uuid()` **sans clé
   métier** et que les triggers sont **append-only**, le résultat est des **liens dupliqués PERMANENTS** (pas
   d'UPDATE/DELETE possible). Le consommateur DOIT donc, **dans la même transaction** que l'écriture des liens et le
   `status→'indexed'`, prendre soit un **`FOR UPDATE` sur la ligne `managed_documents` parente** (ou un
   `pg_advisory_xact_lock` sur l'id), soit écrire les liens sous une **clé déterministe `ON CONFLICT`** (pas de
   `gen_random_uuid()` nu) — la seconde livraison se sérialise alors derrière la première et voit `status='indexed'`.
   Même classe de course que le précédent rigoureux **INV-GED-03 / ADR-0032 §6**. **Acceptance** : un **TEST CONCURRENT
   obligatoire** (deux livraisons **simultanées** ⇒ liens écrits **une seule fois**) ; un test **séquentiel** est un
   **faux-vert** (la course ne se déclenche pas).
3. Le **pont « facture → GED »** est un **consommateur dédié** (qui crée un `ManagedDocument` soft-linké au
   `documents.documents`), **JAMAIS** un abonnement du module `Ged` à `DocumentReceivedV1` (§1, F19 §2.4 NB).

Un **test d'idempotence** (fast-path post-commit + replay du consommateur ⇒ **un seul** `ManagedDocument`, **aucun**
lien dupliqué) est exigé (F19 §8). **JAMAIS un compteur** : l'id du handler est l'unique clé d'idempotence (un compteur
`++` ouvrirait des doublons sur replay).

### 7. Événement et registrar dans `Ged.Contracts` (F19 §2.2 ligne 56, §7 résumé ; RL-17)

`ManagedDocumentReceivedV1` (type d'intégration `ged.managed-document.received`) **et** `GedEventTypeRegistrar` vivent
dans **`Ged.Contracts`** — **PAS** dans `Ingestion.Contracts`, qui est **compile-visible des modules fiscaux** (RL-17).
Mettre l'événement dans `Ingestion.Contracts` exposerait un concern GED à tout le flux fiscal ; le placer dans
`Ged.Contracts` préserve la règle « le flux fiscal ignore la GED » (NetArchTest :
`Pipeline/Validation/Transmission/Documents NotHaveDependencyOn(Ged.*)`). L'outbox existant est **réutilisé** ; seul
l'événement est neuf, écrit dans la même transaction que le registre (§4).

### 8. Mapping déclaratif générique : `DEFER`, jamais deviner (F19 §4.4, §4.5)

Sur la **plateforme** (jamais côté agent), **tenant-scopé, versionné, validé humainement**, en **généralisation du
mapping TVA** existant (noms **figés**) :

| Domaine TVA (existant) | Domaine GED (NEUF, nom figé) | Rôle |
|---|---|---|
| `MappingTable` (`IsValidated`/`Invalidate()`) | `GedMappingProfile` | Profil d'un `documentType`, tenant-scopé, versionné, validé |
| `MappingRule` (lookup → triplet, sinon block) | `AxisMappingRule` / `EntityMappingRule` / `RelationMappingRule` | Source path + condition → axe / entité / relation cible |
| `TvaMapper.Map` → résultat ou **BLOCK** | `GedMapper.Map(profile, ingested)` → `MappedDocument` **ou DEFER** | Range les inconnus au lieu de deviner |
| `MappingChangeLog` (append-only) | `GedMappingChangeLog` (append-only) | Audit immuable des profils |

**Vocabulaire figé : `DEFER`, jamais `BLOCK`.** L'enjeu GED n'est pas fiscal : un `documentType` **sans profil**, ou un
axe **obligatoire non résolu**, range le document en `deferred` (visible console), **jamais mappé au hasard, jamais
rejeté en silence**. Le coffre reste alimentable (le binaire est un fait ; l'indexation peut suivre). Goldens exigés
(F19 §8) : brut → axes attendus, **et** cas DEFER (documentType sans profil, axe requis manquant).

**Résolution d'identité d'entité (F19 §4.4)** : `identity_key` déclaré par `EntityType` (ex. `siret`) ;
**upsert idempotent** par `(entity_type_id, identity_value normalisé)` avant création ; pas de clé → pas de dédup auto.
Fusion de doublons **append-only** (`canonical_id` posé sur l'instance fusionnée, lecture résout la canonique) :
**AUCUNE fusion automatique** — c'est un **geste opéré journalisé** (une fusion erronée est irréversible sous
append-only). On n'utilise qu'un `confidence_score` numérique propre à la GED (`GedMergeConfidence` si une échelle
nommée est nécessaire) — **JAMAIS** le type `MatchConfidence` de `Reconciliation.Domain` (frontière interdite, RL-18),
et **pas** de seuil « High=auto ».

**Invariant montant (CLAUDE.md n°1)** : les valeurs d'axe restent des **chaînes BRUTES** côté agent ; toute
interprétation numérique d'un axe `number` se fait sur la plateforme en `decimal`, arrondi half-up à `value_scale`
(ADR-0032), **jamais** `double`/`float`. Le **parsing des `SourceFields` ambigus** (séparateur décimal, format de date
local) est **déclaré par profil** (format source attendu) et **DEFER si ambigu** — jamais deviner (leçon ODBC DateTime).

## Invariants

- **INV-GED-05** — `DEFER`, **jamais deviner** : un `documentType` sans `GedMappingProfile` validé, ou un axe requis non
  résolu, range le document en `deferred` (visible console) ; **jamais** mappé au hasard, **jamais** rejeté en silence
  (goldens DEFER, F19 §8).
- **INV-GED-06** — **Anti-doublon GED par hash, canal disjoint** : l'anti-doublon `(tenant_id, payload_hash)` est porté
  par le **registre GED dédié `ged_ingestion.ged_received_documents`** (UNIQUE), **JAMAIS** par `ingestion.received_documents`
  (canal fiscal). Le hash est calculé sur `GedCanonicalJson` (`SourceFields` trié par clé ordinal) + `PayloadHasher`
  réutilisé ; golden cross-runtime net48/.NET 10 (RL-39). `GedIngestionDecision` re-copiée dans `Ged.Domain` (golden de
  non-dérive, RL-01), **jamais** une référence à `Ingestion.Domain`.
- **INV-GED-08** (home ADR-0032) — **rappel référencé** : tenant-scope par connexion (l'isolation **EST** la connexion ;
  aucune colonne tenant en base tenant ; aucune requête cross-tenant ; l'agent n'écrit que dans SON tenant, clé API
  scopée). **Ici l'exception documentée** (F19 §3.2(a)) = le **registre `ged_ingestion` en BASE SYSTÈME** (co-localisé
  avec l'outbox), qui porte `tenant_id` — la **seule** façon d'écrire **atomiquement** registre + `ManagedDocumentReceivedV1`
  (pas de 2PC inter-bases, RL-03). L'index GED de la base tenant est peuplé **en aval** par le consommateur.

## Conséquences

**Positif** : un document métier **arbitraire** entre dans la GED **sans toucher une seule surface du flux fiscal** — le
hash du pivot facture reste **bit-identique** (golden), `ingestion.received_documents` est inchangé, `IDocumentIntake`
n'est jamais appelé, `DocumentReceivedV1` n'est jamais écrit ni consommé par `Ged`. Le découplage est **structurel**
(DTO/endpoint/registre/événement disjoints), pas seulement testé. On **réutilise** les primitives déterministes
(`PayloadHasher`, `CanonicalJsonWriter`, le pattern UoW registre+outbox de `PostgresReceivedDocumentUnitOfWork`,
l'outbox, le module `Staging`) **sans franchir une frontière de module** (la décision d'ingestion et la canonisation
sont re-copiées/bâties dans `Ged.*`, pinnées par goldens). L'agent reste **sans logique métier** ; `DEFER` ferme le
faux-vert « mapper au hasard ». Aucun mécanisme transverse nouveau, aucun code `Stratum.*` vendored modifié.

**À la charge du(des) lot(s) d'implémentation** (items GEDxx de F19 §10) :
- **GED05a** (`Liakont.Agent.Contracts.Ged` + `IManagedExtractor` dans `Agent.Core`, add-only ; dépend `GATE_AGENT`) :
  `IngestedDocumentDto`, `ManagedExtractorCapabilitiesDto`, `IManagedExtractor` ; golden **hash pivot facture inchangé**.
- **GED05b** (ingestion plateforme) : `GedCanonicalJson` (sur `CanonicalJsonWriter`, `SourceFields` trié ordinal, golden
  cross-runtime RL-39) ; `GedIngestionDecision` re-copiée dans `Ged.Domain` (golden 3 cas RL-01) ; migration
  `ged_ingestion.ged_received_documents` (base système, UNIQUE `(tenant_id, payload_hash)`) ; UoW registre+outbox
  atomique (calqué `PostgresReceivedDocumentUnitOfWork`) ; endpoint `POST /api/agent/v1/managed-documents/batch` ;
  `ManagedDocumentReceivedV1` + `GedEventTypeRegistrar` dans `Ged.Contracts` ; `staging.WriteAsync` AVANT la tx
  (ADR-0014) ; `IIngestedContentStore` séparé (anti path-traversal) ; fast-path `ON CONFLICT (id) DO NOTHING` +
  consommateur durable écrivant les liens dans la même tx que `status→'indexed'` (idempotence RL-04) ; **pas** d'appel
  à `IDocumentIntake`, **pas** d'abonnement à `DocumentReceivedV1`.
- **Mapping** (généralisation `MappingRule`) : `GedMappingProfile` / `AxisMappingRule` / `EntityMappingRule` /
  `RelationMappingRule` / `GedMapper.Map → MappedDocument ou DEFER` / `GedMappingChangeLog` (append-only) ; résolution
  d'identité (`identity_key`, upsert idempotent, fusion append-only **non auto**, jamais `MatchConfidence` RL-18) ;
  goldens DEFER.
- **Tests** (F19 §8) : golden hash pivot facture inchangé ; golden `GedIngestionDecision` (3 cas, ordre
  doublon-avant-altération, garde hash vide) ; golden mapping (brut→axes + DEFER) ; idempotence (fast-path + replay ⇒ un
  seul `ManagedDocument`, aucun lien dupliqué) ; « aucune ligne `documents.documents`, aucun passage par
  `DocumentReceivedConsumer`, aucun état fiscal atteint » ; NetArchTest agent → `Liakont.Agent.*` (Core/Contracts.Ged)
  jamais le code plateforme, et `Ged.Domain` ne référence pas `Ingestion.Domain` ; l'événement réellement drainé
  (OutboxWorker) et consommé.

**Limite** : cet ADR ne grave **ni** le méta-modèle des axes/entités/liens (ADR-0032), **ni** la surface d'archivage
`IGenericArchiveService` / le coffre tiers (ADR-0033), **ni** l'index de recherche `tsvector` / `document_search`
(ADR-0035), **ni** le `consultation_log` (ADR-0036). Il ne fixe **aucune** valeur de paramétrage tenant (profils,
formats de parsing, `identity_key`) et n'invente **aucune** règle fiscale, légale ou probante.

### Points NON TRANCHÉS (F19 §11 — défaut défendable pris, l'owner tranche, jamais inventé)

| # | Point | Défaut défendable PRIS | Owner |
|---|---|---|---|
| (mapping) | **Langage de sélection** du mapping (`$.fields…`) | ❓ NON TRANCHÉ — reco : **JSONPath restreint** (chemins simples + filtre d'égalité), **pas** un moteur d'expression (aucun calcul dans le mapping). Défaut conservateur, paramétrable ; pas une gate (F19 §4.5) | Archi |
| (mapping) | **Parsing des `SourceFields` ambigus** (séparateur décimal, format de date local) | ❓ NON TRANCHÉ — **déclaré par profil** (format source attendu) ; **DEFER si ambigu**, jamais deviner (cf. leçon ODBC DateTime, F19 §4.5) | tenant (paramétrage) |
| D11 | **Multilingue** du contenu indexé (touche la source de texte ingérée) | V1 `french` (FR-only aligné F10) ; contenu non-FR best-effort ; multilingue fast-follow (F19 §11) | Produit |

Aucun de ces points ne stalle le dev : ce sont des **défauts paramétrables**, pas des gates. La discipline est
**toujours DEFER** en cas d'ambiguïté (jamais une supposition).

## Alternatives rejetées

- **Surcharger `PivotDocumentDto` d'un champ GED** : casse la stabilité octet du hash fiscal (ADR-0007), faux
  `AcceptedAltered` sur facture inchangée. **Rejetée** — DTO disjoint `IngestedDocumentDto` (§1/§2).
- **Partager `ingestion.received_documents`** : index `(tenant_id, payload_hash)` **sans discriminant de canal** → faux
  `Duplicate`/`AcceptedAltered` sur le canal fiscal. **Rejetée** — registre GED dédié en base système (§4, RL-03).
- **Référencer `Ingestion.Domain` depuis `Ged.Domain`** (pour réutiliser `DocumentIngestionDecision`) : viole la
  frontière inter-modules (CLAUDE.md n°6, NetArchTest). **Rejetée** — logique **re-copiée** comme `GedIngestionDecision`,
  golden de non-dérive (RL-01).
- **Mettre `ManagedDocumentReceivedV1` dans `Ingestion.Contracts`** : `Ingestion.Contracts` est compile-visible des
  modules fiscaux → expose un concern GED au flux fiscal. **Rejetée** — événement et registrar dans `Ged.Contracts`
  (§7, RL-17).
- **Appeler `IDocumentIntake`** (ou s'abonner à `DocumentReceivedV1`) pour un document GED : créerait un `Document`
  fiscal sans contrepartie et l'injecterait dans le pipeline d'émission. **Rejetée** — sortie = `ManagedDocument`
  uniquement ; le pont facture→GED est un consommateur dédié, jamais un abonnement de `Ged` à l'événement fiscal (§1/§6).
- **Fusion automatique d'entités** par seuil de confiance : irréversible sous append-only, risque de fusion erronée.
  **Rejetée** — fusion = **geste opéré journalisé** (`canonical_id` append-only) ; jamais `MatchConfidence` de
  `Reconciliation.Domain` (RL-18), jamais « High=auto » (§8).
- **Compteur d'idempotence** (au lieu de l'id du `ManagedDocument`) : un replay ou un fast-path concurrent ouvrirait des
  doublons. **Rejetée** — id attribué par le handler, porté dans l'événement, `ON CONFLICT (id) DO NOTHING` ; liens
  écrits par le **seul** consommateur durable dans la même tx que `status→'indexed'` (§6, RL-04).
- **Émettre `SourceFields` non trié** : ordre d'énumération de dictionnaire non déterministe cross-runtime → hash
  instable → anti-doublon faux-vert. **Rejetée** — `SourceFields` **trié par clé (ordinal)** par `GedCanonicalJson`,
  golden cross-runtime net48/.NET 10 (§2, RL-39).
- **Fondre `IIngestedPdfStore`** pour le contenu GED : le pool de réconciliation est un concern fiscal à sémantique
  d'écrasement. **Rejetée** — `IIngestedContentStore` séparé, anti path-traversal conservé (§5).

## Références

- `docs/conception/F19-GED-Dynamique-Coffre-Fort.md` §2.4 (flux MVP, deux canaux), §3.2 (db-per-tenant et exceptions),
  **§4 complet** (§4.1 coexistence des canaux, §4.2 `IngestedDocumentDto`, §4.3 réutilisation exacte + §4.3.1 registre
  GED dédié + §4.3.2 contenu + §4.3.3 version de contrat, §4.4 résolution d'identité, §4.5 mapping déclaratif, §4.6 côté
  agent), §8 (goldens `GedIngestionDecision` RL-01, mapping, idempotence RL-04, hash-neutralité facture), §10 (items
  GED05a/GED05b), §11 (décisions ouvertes).
- ADR GED liés : **ADR-0032 — Méta-modèle GED dynamique : axes typés et entités polymorphes append-only (anti-EAV),
  module unique `Liakont.Modules.Ged` à trois schémas PostgreSQL** (crée `ManagedDocument` + liens) ; **ADR-0033 —
  Coffre probant tiers / SAE comme 5ᵉ axe enfichable (`ISealedArchiveProvider`) et archivage WORM des documents GED hors
  chaîne fiscale (option C ; fast-follow GED20)** (le consommateur archive via `IGenericArchiveService`) ; **ADR-0035 —
  Recherche & index GED : `tsvector` PostgreSQL derrière `IDocumentSearchIndex`, projection asynchrone reconstructible,
  graphe borné bidirectionnel** (projette le `search_vector`) ; **ADR-0036 — Journal de consultation GED append-only
  (`ged_index.consultation_log`, base tenant, WORM) : best-effort par défaut, fail-closed si finalité probante**.
- ADR socle : `docs/adr/ADR-0007-serialisation-canonique-pivot.md` (sérialisation canonique du pivot, add-only de
  contrat) ; `docs/adr/ADR-0014-staging-durable-contenu-pivot-intake.md` (staging durable, ordre d'écriture) ;
  `docs/adr/ADR-0006-mecanique-jobs-multi-tenant.md` (mécanique des jobs multi-tenant, outbox/drain) ;
  `docs/adr/ADR-0016-job-tenant-scope.md` (job tenant-scopé).
- Code réel imité : `src/Modules/Ingestion/Infrastructure/Migrations/V004__create_received_documents_table.sql` (registre
  `(tenant_id, payload_hash)` UNIQUE, `contract_version text NOT NULL`) ; `PostgresReceivedDocumentUnitOfWork` (registre
  + outbox dans la même transaction, base système) ; `CanonicalJsonWriter` (ordre figé, null omis, ASCII, enums par
  nom) ; `PayloadHasher.ComputeHash(string)` (SHA-256 des octets) ; `DocumentIngestionDecision.Evaluate`
  (`Ingestion.Domain`, 3 cas) ; `IDocumentIntake` ; `IIngestedPdfStore` ; module `Staging` (`Staging.Contracts`).
