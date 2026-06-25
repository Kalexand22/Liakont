# ADR-0033 — Coffre probant tiers / SAE comme 5ᵉ axe enfichable (`ISealedArchiveProvider`) et archivage WORM des documents GED hors chaîne fiscale (option C ; fast-follow GED20)

- **Statut** : Proposé (2026-06-25).
- **Date** : 2026-06-25
- **Nature** : cet ADR **précède** le chantier d'implémentation (module `Liakont.Modules.Ged` non démarré,
  **aucun code**). Les sections **Décision** et **Invariants** sont **normatives** : elles décrivent la **cible**,
  pas l'état du code. Aucun invariant n'est garanti tant qu'il n'est pas livré **et** prouvé par test. Cet ADR
  **dérive de** la conception `docs/conception/F19-GED-Dynamique-Coffre-Fort.md` (statut « proposition NON
  RATIFIÉE ») et n'invente **aucune** règle fiscale, légale ou probante (CLAUDE.md n°2). Il est une **sœur**
  d'ADR-0032 (méta-modèle GED), d'ADR-0034 (ingestion générique), d'ADR-0035 (recherche & index) et d'ADR-0036
  (journal de consultation) : il tranche **où et comment ranger un document GED** et **comment brancher un coffre
  probant tiers**, sans toucher au coffre fiscal souverain ni au flux e-invoicing.
- **Numérotation** : ADR-**0033**. La numérotation libre de la GED (F19 §9) commence à **0032** (le repo contient
  déjà DEUX `ADR-0031` — `-cablage-cycle-run-agent…` et `-licence-fluentassertions…`). Plan d'ADR GED : **0032**
  méta-modèle, **0033** coffre tiers / option C (fast-follow GED20), **0034** ingestion générique, **0035**
  recherche & index, **0036** journal de consultation.
- **Contexte décisionnel** : `docs/conception/F19-GED-Dynamique-Coffre-Fort.md` §1.2/§1.3 (frontières du coffre),
  §2.2/§2.3 (composants neufs + 5ᵉ axe de plug-in), §5 **complet** (5.1 surface générique hors chaîne fiscale,
  5.2 `ISealedArchiveProvider`, 5.3 stockage hybride, 5.4 `sealed_refs`, 5.5 job de réplication, 5.6 vérifieur &
  export ; INV-ARCH-GED-1/2/3), §10 (V1 vs fast-follow), §11 D1/D2/D3/D4 (décisions ouvertes). Sources socle /
  code réelles citées par F19 : `src/Modules/Documents/Infrastructure/Migrations/V005__create_archive_entries_table.sql`
  (`document_id` `NOT NULL` + FK vers `documents.documents`, triggers WORM `reject_archive_entry_mutation` +
  no-truncate), `src/Modules/Archive/Infrastructure/PostgresArchiveEntryStore.cs` (chaîne **globale par tenant**,
  un seul `HeadSql`/verrou `0x41524348`), `src/Modules/Archive/Application/FiscalControlExportService.cs:103-109`
  (filtre de période `{année}/{mois}/…`), `src/Modules/Archive/Contracts/IArchiveService.cs`
  (`ArchiveIssuedDocumentAsync`/`AddAddendumAsync`, facture-spécifique), `src/Modules/Archive/Domain/IArchiveStore.cs`
  + `ArchiveStoreCapabilities.cs` (rangement d'octets write-once WORM, capacités déclarées). ADR liés : ADR-0009
  (`IArchiveStore` WORM FS + S3), ADR-0011 (ancrage RFC 3161), ADR-0032/0034/0035/0036 (sœurs GED).

## Contexte

La GED Liakont est une **couche d'indexation métier** posée **au-dessus** du coffre probant fiscal déjà en
production (F19 §1.1). Elle doit ranger trois natures d'objets : (a) des **factures** EN 16931 déjà scellées par
le flux fiscal — la GED ne fait que les **pointer** (soft-link) ; (b) des **documents purement métier** (PV,
contrats, courriers, bordereaux, situations de travaux) qui n'ont **aucune** contrepartie fiscale ; (c) à terme,
une **réplique scellée chez un tiers probant** (SAE / Arkhineo, NF Z42-013). La question tranchée ici est : **où et
comment range-t-on un document GED, et comment branche-t-on un coffre tiers, sans jamais corrompre le coffre fiscal
souverain ?**

Le piège central est structurel et concerne la **chaîne de hashes fiscale**. La table `documents.archive_entries`
(possédée par le module `Documents`, `V005`) a `document_id` **`NOT NULL` + FK** vers `documents.documents`
(`V005:11,18-20`), et `IArchiveEntryStore.ReserveAsync(Guid documentId, …)` est non-nullable. Pire : la chaîne est
**globale par tenant** — `PostgresArchiveEntryStore` n'a qu'un seul `HeadSql` et un seul verrou consultatif
`0x41524348`, et `chain_hash(N) = SHA256(chain_hash(N-1) + package_hash(N))`. Y insérer un document GED-seul
exigerait d'inventer un faux `documents.documents` (interdit : aucune ligne fiscale pour un objet non fiscal) **et**
**mélangerait** les maillons fiscaux et GED dans une seule chaîne : une corruption d'un document GED casserait alors
la **vérification fiscale**, et le `chain_hash` des factures suivantes dépendrait de l'activité GED. Une erreur ici
engage la responsabilité fiscale du client (CLAUDE.md). Le coffre fiscal — `archive_entries`, `HashChain`,
`ArchiveVerifier`, ancrage RFC 3161 (ADR-0011) — doit donc rester **strictement souverain et intouché**.

La seconde force est la **valeur probante renforcée**. Un SAE tiers (Arkhineo / NF Z42-013) n'est **pas** un
stockage d'octets que nous adressons : il **scelle** selon sa politique, **attribue** une référence opaque,
**retourne** une preuve, et a un **cycle asynchrone** (versement → pending → sealed). Le forcer dans l'abstraction
de stockage `IArchiveStore` (ADR-0009 : `WriteAsync→Task` write-once, lecture par chemin) serait sémantiquement
faux. Et la valeur **juridique** d'une attestation tierce (NF Z42-013, vérification autonome) **n'est pas tranchée**
(F19 §11 D1/D2) : affirmer « conforme NF Z42-013 » sans spécification de vérification autonome ratifiée serait
**inventer une règle probante** (CLAUDE.md n°2). « Bloquer / déférer plutôt qu'affirmer faux. »

La décision retient donc l'**option C** (F19 redline RL-05) : un document GED-seul est rangé **write-once WORM** via
`IArchiveStore` dans un espace dédié `_ged/…`, **hors** de la chaîne de hashes fiscale, **sans** ancrage RFC 3161
en V1 ; la valeur probante renforcée est **déférée** à un fast-follow (GED20). Pour éviter le **code dormant**
(RL-26), l'abstraction de coffre tiers `ISealedArchiveProvider` et sa table de référence ne sont **pas** posées en
V1 : elles arrivent **avec** GED20 et son premier provider. En V1, les seules abstractions à capacités réellement
livrées sont `IGenericArchiveService` (rangement WORM hors chaîne fiscale) et `IDocumentSearchIndex` (recherche,
cf. ADR-0035).

## Décision

### 1. Option C (RL-05) — un document GED-seul n'entre JAMAIS dans `archive_entries` ; rangement WORM hors chaîne fiscale

Un document GED **purement métier** n'a **aucune ligne** `documents.documents` ; or `documents.archive_entries.document_id`
est `NOT NULL` + FK (`V005:11,18-20`), `IArchiveEntryStore.ReserveAsync` est non-nullable, et la chaîne est
**globale par tenant** (un seul `HeadSql`/verrou `0x41524348`, `PostgresArchiveEntryStore`). **On n'insère donc
JAMAIS un document GED-seul dans `archive_entries`.** Il est rangé **write-once WORM** via `IArchiveStore` (ADR-0009)
sous l'arborescence dédiée **`_ged/{kind}/{année}/{mois}/{clé}/`** — un **espace d'octets WORM séparé, HORS de la
chaîne de hashes fiscale** et **sans ancrage RFC 3161 en V1**.

Le coffre fiscal — `documents.archive_entries`, `HashChain`, `ArchiveVerifier`, ancrage RFC 3161 (ADR-0011) — n'est
**ni touché ni étendu**. Une **facture** reste scellée par le flux fiscal existant (`ArchiveIssuedDocumentAsync`,
chaîne `{année}/{mois}/{clé}/`) ; la GED ne fait que la **POINTER** via un soft-link (`fiscal_document_id` /
`archive_entry_id` / `archive_path`, sans FK cross-schéma ; cf. ADR-0032). L'intégrité locale d'un document GED en
V1 = **rangement write-once WORM** + `content_hash` (SHA-256) **indexé** dans `ged_index.managed_documents`
(ADR-0032 §3.4.1) ; l'ancre d'intégrité de **référence** sont les **octets write-once** de `IArchiveStore` (vraiment
immuables), le `content_hash` n'en étant qu'une **copie indexée**.

### 2. Surface d'archivage générique `IGenericArchiveService` (Archive.Contracts, additive, hash-neutre)

`IArchiveService` est **facture-spécifique** (`ArchiveIssuedDocumentAsync` exige `DocumentId` FK, `PaResponseJson`,
le `Readable` d'une facture…). On **n'étend pas** cette méthode (la stabilité octet du hash fiscal en dépend). On
ajoute une **surface générique distincte**, **additive** et **hash-neutre pour la facture** :

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

public readonly record struct ArchiveIndexAxis(string AxisCode, string? Value, bool IsConfidential);   // Value=null si IsConfidential (RL-19)
```

- **`ArchiveKind` est une valeur produit GÉNÉRIQUE** (jamais `'lot'`/`'vente'`/`'pv'` : la nature métier est un
  **axe de tenant**, ADR-0032 ; aucune table/axe/entité métier en dur, F19 §1.2). `ArchiveKey` remplace
  `DocumentNumber`, `FiledOn` remplace `IssueDate`.
- **Frontière inter-modules (P1) :** `Archive.Contracts` **ne référence JAMAIS** `DocumentAxisLink` (type du module
  GED). `IndexAxes` est une **projection plate locale** `ArchiveIndexAxis` ; la couche GED convertit ses
  `DocumentAxisLink` vers cette projection **au point d'appel** (pattern « projections locales, aucun
  `Contracts → Contracts` d'un autre module », API01c). Référencer `DocumentAxisLink` depuis `Archive.Contracts`
  serait une violation de frontière (CLAUDE.md n°6/14).
- **RL-19 — valeur confidentielle JAMAIS figée en clair :** pour un axe `IsConfidential`, `Value` **DOIT** être
  `null` ; `IndexAxes` ne porte alors que `AxisCode` + `IsConfidential`. Le manifest WORM ne gèle **aucune** valeur
  confidentielle en clair — un axe requalifié confidentiel **après** scellement resterait sinon en clair de manière
  **irréversible** (le WORM interdit toute réécriture). Le coffre n'est **pas** un canal de contournement du
  masquage (le chiffrement-au-repos d'une valeur confidentielle indexée reste **D9 ouvert**).

### 3. Hash-neutralité STRUCTURELLE (pas seulement « prouvée par test »)

L'**arborescence** sépare les deux espaces : factures sous `{année}/{mois}/{clé}/` (chaîne fiscale, **inchangée**) ;
documents GED sous **`_ged/{kind}/{année}/{mois}/{clé}/`** (espace d'octets WORM séparé, hors chaîne). Sous l'option
C, un document GED-seul n'a **aucune** ligne `documents.archive_entries`, donc `FiscalControlExportService` — qui
n'énumère que la **chaîne fiscale** via `IArchiveEntryStore.GetChainAsync` puis filtre sur le préfixe de période
`{année}/{mois}/…` (`FiscalControlExportService.cs:103-109`) — **exclut structurellement** les paquets `_ged/…`
d'un export de **contrôle fiscal** (un chemin `_ged/…` ne commence jamais par `{année}/`). L'export de
**réversibilité GED** (ADR-0035), lui, énumère le coffre GED (`_ged/…`) et les **inclut**.

La hash-neutralité des factures est ainsi **structurelle** : la GED **ne partage pas** la chaîne fiscale (pas
seulement « le test golden constate que le hash est inchangé »). Le test golden de GED07 vérifie qu'aucune migration
ni surface GED ne touche la table `archive_entries` ni le chemin d'écriture fiscal, et que le hash d'un paquet
**facture** est **inchangé** par la livraison GED.

### 4. 5ᵉ axe de généricité — coffre probant tiers / SAE via `ISealedArchiveProvider` (sœur d'`IArchiveStore`, pas `IDocumentVault`)

Les quatre axes de généricité enfichables existants sont : (1) source `IExtractor`, (2) PA `IPaClient` +
`PaCapabilities`, (3) stockage archive `IArchiveStore` + `ArchiveStoreCapabilities`, (4) IdP. Le **5ᵉ axe (NEUF)**
est le **coffre probant tiers / SAE** via `ISealedArchiveProvider` + `SealedArchiveCapabilities` — une **abstraction
SŒUR DISTINCTE** d'`IArchiveStore`, **dans `Archive.Domain`**. Un SAE tiers n'est **pas** un stockage d'octets
adressé par nous ; sa sémantique est « **scellement + référence opaque + preuve (peut être pending)** » avec un
cycle **asynchrone** `Seal` / `Poll` / `RetrieveProof`. On **supprime** le `IDocumentVault` unifié des brouillons
(il mélangeait à tort stockage d'octets et scellement).

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

- Le comportement est **piloté par les capacités déclarées** du provider (`SealedArchiveCapabilities`), jamais par
  un test de type concret : **`if (provider is Arkhineo)` est interdit (P1, CLAUDE.md n°8/14)**. Le plug-in
  (`Archive/SealedProviders/Arkhineo`…) ne référence que `Archive.Contracts`/`Domain`, jamais un autre plug-in ni un
  module métier (CLAUDE.md n°6).
- Le **choix du provider** est de la **configuration d'instance/tenant**. Le **secret** d'accès (clé API /
  certificat) est **chiffré par tenant en base** (CLAUDE.md n°10) ; le code n'embarque que des **exemples fictifs**
  (`config/exemples/`, CLAUDE.md n°7).

### 5. FAST-FOLLOW GED20 — pas de code dormant en V1 (RL-26)

`ISealedArchiveProvider`, `SealedArchiveCapabilities`, le plug-in provider concret, la table de référence, le job de
réplication, le vérifieur et l'export sont **posés par GED20 AVEC le premier provider**, **PAS en V1** (anti-code
dormant, RL-26 + option C). En V1, seules `IGenericArchiveService` (rangement WORM hors chaîne fiscale) et
`IDocumentSearchIndex` (recherche, ADR-0035) sont livrées.

**Table de référence `ged_index.sealed_refs`** (GED20, schéma `ged_index` du module `Ged`, **base tenant**,
append-only WORM). Sous l'option C, un document GED-seul n'a **aucune** ligne `documents.archive_entries` : la
référence de scellement **ne peut donc PAS** être une FK vers `archive_entries`. Elle référence l'**objet archivé
par son chemin WORM** (`archive_path` : paquet fiscal `{année}/…` OU GED `_ged/…`). On **écarte** toute colonne/FK
sur `archive_entries` (incompatible avec un document GED-seul **et** avec le trigger WORM qui rejette tout UPDATE).
Cette table sort du schéma `documents` (déjà sous double-V010) ; son numéro concret sera alloué par GED20 dans la
séquence de migrations `ged_index` neuve.

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
-- triggers reject_*_mutation (UPDATE/DELETE) + no_truncate : COPIE EXACTE du pattern archive_entries.
```

- **État courant** = **dernière ligne** par `archive_path` selon **`seq`** (déterministe, jamais `recorded_utc`).
  **Idempotence par clé métier** `(archive_path, provider)`, comme `archive_anchors` sur `(head, method)`.
  `external_provider` est l'**identifiant de capacité résolu via le registre** (jamais une valeur libre saisie) ;
  toute ligne dont le provider n'est plus résolvable est traitée `SealClaimedNotVerifiable`, **jamais ignorée**.
- **Job `SealedReplicationTenantJob : ITenantJob`** (`Name => "archive.sealed-replication"`, fan-out par tenant
  délégué à `TenantJobRunner`, module-rules §6) + **`SealedReplicationService`** (WORM-safe), calqués sur
  `DailyAnchoringTenantJob` : (1) court-circuit par capacité (`SupportsSealing == false` → ligne `unsupported`,
  jamais un faux vert) ; (2) idempotence (lecture de la dernière ligne) ; (3) `SealAsync` → si `IsAsynchronousSeal`,
  `pending` puis `PollAsync` au tour suivant ; (4) au `sealed` : `RetrieveProofAsync` → **preuve rangée write-once
  (WORM) à côté du paquet** via `IArchiveStore` → insère la ligne `sealed` avec `proof_path`.
- **Vérifieur & export** : la preuve étant rangée write-once dans le coffre WORM à côté du paquet, sa présence et son
  empreinte sont vérifiables ; **statuts PRUDENTS** `SealVerified` / `SealClaimedNotVerifiable` / `SealMissing` (sur
  le modèle `NotVerifiable` des ancrages : une preuve non re-vérifiable est **non vérifiable, jamais invalide**).
  Export `references-coffre-tiers.json` **filtré sur exactement les `archive_path` retenus par l'export en cours**
  (jointure sur la sélection, jamais un dump global) ; notice : « scellé chez un tiers déclarant NF Z42-013, **non
  re-vérifié par Liakont** », **jamais** « conforme NF Z42-013 » (cf. INV-ARCH-GED-3, D1/D2).

## Invariants

- **INV-ARCH-GED-1 (option C, hash-neutralité structurelle)** — un document GED-seul est rangé **write-once (WORM)**
  via `IArchiveStore` (aucun chemin d'update/delete) **mais n'entre pas** dans la chaîne de hashes fiscale et **ne
  modifie ni n'étend** `documents.archive_entries` / `ArchiveVerifier`. La hash-neutralité des factures est
  **structurelle** (la GED ne partage pas la chaîne fiscale), pas seulement « prouvée par test ». Test golden : aucune
  migration ni surface GED ne touche la table ni le chemin d'écriture fiscal ; hash d'un paquet **facture** inchangé ;
  `_ged/…` exclu d'un export de contrôle fiscal.
- **INV-ARCH-GED-2 (intégrité de référence)** — pour une **facture**, la valeur probante de référence reste **chaîne
  de hashes + ancrage RFC 3161** (souveraine, inchangée). Pour un **document GED**, l'intégrité locale de référence
  est le **rangement write-once WORM** + `content_hash` indexé. Toute valeur probante renforcée (coffre tiers,
  NF Z42-013, ou chaîne GED dédiée ultérieure) est **déférée à un fast-follow** et **n'est jamais affirmée** tant que
  non livrée et confirmée (CLAUDE.md n°3). Jamais bloquer le flux sur l'indisponibilité d'un coffre tiers
  (CLAUDE.md n°8).
- **INV-ARCH-GED-3 (NF Z42-013 déclaratif ≠ affirmation de conformité)** — `ClaimsNfZ42013 == true` (déclaratif
  fournisseur) **n'autorise jamais à lui seul** un affichage / une notice « conforme NF Z42-013 ». Seul
  `SealVerified` **+** une spécification de vérification autonome ratifiée (D1) le permet. Tant que D1 non tranché :
  au plus « scellé chez un tiers déclarant ».

## Conséquences

**Positif** : le coffre fiscal souverain (`archive_entries`, `HashChain`, `ArchiveVerifier`, ancrage RFC 3161) reste
**intouché et protégé** — une corruption d'un document GED ne peut **structurellement** pas casser la vérification
fiscale (chaînes disjointes). La généricité du coffre est portée par une **surface additive hash-neutre**
(`IGenericArchiveService`) et un **5ᵉ axe à capacités** (`ISealedArchiveProvider`) cohérents avec la doctrine
« abstraction d'abord, plug-in ensuite » (historique `IArchiveStore` → S3). En **n'introduisant aucun code dormant
en V1** (le port tiers arrive avec son premier provider GED20), on évite une abstraction non exercée. Le branchement
d'un SAE tiers est **piloté par capacités** (pas de couplage à un fournisseur concret) ; les statuts prudents et la
notice non affirmative évitent toute affirmation probante non sourcée.

**À la charge du(des) lot(s) d'implémentation** (items GEDxx de F19 §10) :
- **GED07** (V1) : `IGenericArchiveService.ArchiveManagedDocumentAsync` / `AddManagedAddendumAsync` (Archive.Contracts,
  additif, hash-neutre) + adaptateur de rangement qui écrit les octets **write-once via `IArchiveStore` sous
  `_ged/…`** et **n'insère RIEN** dans `archive_entries` ; projection plate `ArchiveIndexAxis` (RL-19 :
  `Value=null` si confidentiel) ; tests : rangement idempotent, **aucune** ligne `archive_entries` pour un document
  GED-seul, hash d'un paquet **facture** inchangé (golden), `_ged/…` exclu de l'export de contrôle fiscal,
  NetArchTest `Ged → Archive.Contracts` seulement (jamais `Archive.Domain`/store concret ; `Archive.Contracts` ne
  référence pas `DocumentAxisLink`).
- **GED20** (fast-follow, **blocked sur D1**) : `ISealedArchiveProvider` + `SealedArchiveCapabilities`
  (`Archive.Domain`), **premier provider concret** (plug-in), migration `ged_index.sealed_refs` (append-only WORM,
  triggers `reject_*_mutation` + no-truncate), `SealedReplicationTenantJob` + `SealedReplicationService`
  (calqués `DailyAnchoringTenantJob`), vérifieur + export `references-coffre-tiers.json` (statuts
  `SealVerified`/`SealClaimedNotVerifiable`/`SealMissing`, notice non affirmative) ; secret provider chiffré par
  tenant ; tests : court-circuit par capacité (`unsupported`), idempotence `(archive_path, provider)`, cycle
  asynchrone seal→pending→sealed, preuve rangée WORM, `if (provider is X)` interdit.

**Limite** : cet ADR ne grave **ni** la valeur probante juridique d'une attestation tierce ou son format de
vérification autonome (D1), **ni** le droit de Liakont à revendiquer NF Z42-013 (D2), **ni** la politique de
rétention/RGPD d'un document GED non fiscal sous WORM (D3, qui suppose le prérequis chiffrement-au-repos D9), **ni**
le périmètre exact V1 (D4). Il ne pose **aucun provider concret en V1** (GED20). Le **consommateur** qui appelle
`ArchiveManagedDocumentAsync` après indexation est porté par ADR-0034 (ingestion générique) ; l'export de
réversibilité GED par ADR-0035.

### Points NON TRANCHÉS (F19 §11 — défaut défendable pris, l'owner tranche, jamais inventé)

| # | Point | Défaut défendable PRIS | Owner |
|---|---|---|---|
| D1 | Valeur **probante** d'une attestation tierce (Arkhineo / NF Z42-013) et format de **vérification autonome** | ❓ NON TRANCHÉ — tant que non spécifié : statut `SealClaimedNotVerifiable` ; jamais « conforme NF Z42-013 » ; ceinture + bretelles (ancrage produit souverain toujours actif) | EC + juridique |
| D2 | Liakont peut-il **revendiquer** NF Z42-013 via un SAE tiers, ou seulement « scellé chez un tiers certifié » ? | ❓ NON TRANCHÉ — ne revendiquer que ce que le fournisseur **atteste** (notice « scellé chez un tiers déclarant », non re-vérifié par Liakont) | Juridique |
| D3 | **Rétention / cycle de vie d'un document GED NON fiscal** sous WORM inaltérable ↔ droit à l'effacement RGPD (art. 17) | ❓ NON TRANCHÉ — `retention_class` par document (`legal_hold`/`tenant_bounded`/`erasable`) ; le **crypto-shredding** **suppose une couche de chiffrement-au-repos du contenu QUI N'EXISTE PAS** (`IArchiveStore` écrit des octets BRUTS, RL-06) → c'est un **prérequis (D9)**, pas un mécanisme V1 ; par défaut sûr = **pas de purge auto** | DPO + juridique |
| D4 | **Périmètre V1** : MVP strict (coffre Liakont seul) ou coffre tiers dès V1 ? | ✅ **TRANCHÉ (Karl, 2026-06-25) — MVP strict V1** (coffre WORM Liakont seul) ; coffre tiers en fast-follow GED20 (dépend de D1) | Karl |

Aucun de ces points ne stalle la **tranche V1** : le coffre WORM Liakont (GED07) ne dépend d'aucun d'eux ; seuls les
items fast-follow GED20+ sont conditionnés (notamment **blocked sur D1**). Le défaut sûr partout = **ne jamais
affirmer une valeur probante non livrée et non confirmée**, et **ne jamais purger** sous WORM.

## Alternatives rejetées

- **Insérer un document GED-seul dans `documents.archive_entries`** (en réutilisant la FK `document_id`) : impossible
  sans inventer une fausse ligne `documents.documents`, **et** la chaîne étant **globale par tenant** (un seul
  `HeadSql`/verrou `0x41524348`, `chain_hash(N)=SHA256(chain_hash(N-1)+package_hash(N))`), une corruption d'un
  document GED casserait la **vérification fiscale** et lierait le `chain_hash` des factures à l'activité GED.
  **Rejetée** — chaînes disjointes, option C.
- **Étendre `ArchiveIssuedDocumentAsync`** (ou `IArchiveService`) pour absorber le générique : casserait la stabilité
  octet du hash fiscal d'une facture (champs facture-spécifiques : `DocumentId` FK, `PaResponseJson`, `Readable`).
  **Rejetée** — surface **additive** `IGenericArchiveService`, méthode facture inchangée.
- **Forcer un SAE tiers dans `IArchiveStore`** (stockage d'octets, `WriteAsync→Task` write-once, lecture par chemin) :
  faux sémantiquement — un SAE **scelle** selon sa politique, attribue une **référence opaque**, retourne une
  **preuve** et a un **cycle asynchrone**. **Rejetée** — abstraction **sœur distincte** `ISealedArchiveProvider`.
- **`IDocumentVault` unifié** (des brouillons) : mélangeait stockage d'octets (`IArchiveStore`) et scellement tiers
  (`ISealedArchiveProvider`). **Rejetée** — deux abstractions distinctes ; `IDocumentVault` supprimé.
- **FK `ged_index.sealed_refs → documents.archive_entries`** : incompatible avec un document GED-seul (aucune ligne
  `archive_entries`) **et** avec le trigger WORM qui rejette tout UPDATE. **Rejetée** — référence par
  `archive_path` WORM (pas de FK), état courant = dernière ligne par `seq`.
- **Coupler l'intégrité GED au WORM natif du backend de stockage** (`if (store is S3)`) : l'intégrité produit reste
  **indépendante du backend** (blueprint règle 6 ; ancre = octets write-once de `IArchiveStore` + `content_hash`
  indexé). **Rejetée** — capacités déclarées, jamais un test de backend concret.
- **Poser `ISealedArchiveProvider` + `sealed_refs` + job dès la V1** (sans provider) : **code dormant** non exercé
  (RL-26). **Rejetée** — l'abstraction tiers arrive **avec** GED20 et son premier provider ; en V1 seules
  `IGenericArchiveService` et `IDocumentSearchIndex` sont livrées.

## Références

- `docs/conception/F19-GED-Dynamique-Coffre-Fort.md` §1.2/§1.3 (frontières du coffre, découplage e-invoicing),
  §2.2/§2.3 (composants neufs + 5ᵉ axe de plug-in), §5.1–§5.6 (surface générique hors chaîne fiscale,
  `ISealedArchiveProvider`, stockage hybride, `sealed_refs`, job de réplication, vérifieur & export ;
  INV-ARCH-GED-1/2/3), §10 (V1 vs fast-follow, items GEDxx), §11 D1/D2/D3/D4/D9 (décisions ouvertes).
- ADR GED sœurs : **ADR-0032** — Méta-modèle GED dynamique : axes typés et entités polymorphes append-only
  (anti-EAV), module unique `Liakont.Modules.Ged` à trois schémas PostgreSQL (`managed_documents`, `archive_path`,
  `content_hash`) ; **ADR-0034** — Canal d'ingestion générique GED par agents :
  `IngestedDocumentDto` / `ManagedDocumentReceivedV1` add-only, registre dédié en base système, `IManagedExtractor`
  distinct (le consommateur appelle `ArchiveManagedDocumentAsync` après indexation) ; **ADR-0035** — Recherche &
  index GED : `tsvector` PostgreSQL derrière `IDocumentSearchIndex`, projection asynchrone reconstructible, graphe
  borné bidirectionnel (export / réversibilité GED) ; **ADR-0036** — Journal de consultation GED append-only
  (`ged_index.consultation_log`, base tenant, WORM).
- ADR socle / Liakont : **ADR-0009** (`IArchiveStore` WORM FS + S3, intégrité indépendante du backend) ;
  **ADR-0011** racine (ancrage temporel RFC 3161 / OpenTimestamps — à ne pas confondre avec l'ADR-0011 socle
  database-per-tenant) ; **ADR-0006** (mécanique de jobs multi-tenant, `ITenantJob` / `TenantJobRunner`) ;
  **ADR-0007** (sérialisation canonique du pivot et empreinte de payload, PIV02).
- Code réel : `src/Modules/Documents/Infrastructure/Migrations/V005__create_archive_entries_table.sql`
  (`document_id` `NOT NULL` + FK `documents.documents`, triggers WORM `reject_archive_entry_mutation` +
  no-truncate) ; `src/Modules/Archive/Infrastructure/PostgresArchiveEntryStore.cs` (chaîne globale par tenant,
  `HeadSql`/verrou `0x41524348`) ; `src/Modules/Archive/Application/FiscalControlExportService.cs:103-109`
  (filtre de période `{année}/{mois}/…`) ; `src/Modules/Archive/Contracts/IArchiveService.cs`
  (`ArchiveIssuedDocumentAsync` facture-spécifique) ; `src/Modules/Archive/Domain/IArchiveStore.cs` +
  `ArchiveStoreCapabilities.cs` (octets write-once, capacités déclarées).
- Normes / textes (cités par F19, non re-interprétés ici) : NF Z42-013 (archivage électronique probant) ;
  règlement UE 910/2014 (eIDAS, horodatage qualifié) ; RGPD art. 17 (droit à l'effacement) — tous renvoyés à leurs
  owners (D1/D2/D3) sans affirmation produit.
