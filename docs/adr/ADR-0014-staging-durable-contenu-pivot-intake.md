# ADR-0014 — Staging durable du contenu à l'intake : la plateforme détient le pivot pour le pipeline

**Date :** 2026-06-05

**Statut :** Accepté (2026-06-05)

**Amende :** ADR-0012 (rôle de l'agent : « tampon détenteur unique » → « filet de sécurité » ;
la plateforme devient le détenteur durable du contenu pendant le traitement). F12 §2.3 / §4
(rôle de la file locale de l'agent).

---

## Contexte

Le pipeline de traitement (PIP01 : CHECK → SEND → SYNC) exige le **pivot COMPLET** à chaque
étape — lignes, ventilation TVA ligne par ligne, parties :

- le **mapping TVA** consomme la ventilation **par-ligne** (`PivotLineDto.SourceRegimeCodes :
  IReadOnlyList<string>`, `src/Contracts/Liakont.Agent.Contracts/Pivot/PivotLineDto.cs`) — le
  consommateur qui itère ligne par ligne est le travail de PIP01 (`MappingRequest` ne porte
  aujourd'hui qu'un code unitaire) ;
- la **validation** prend le `PivotDocumentDto` entier
  (`src/Modules/Validation/Contracts/DocumentValidationContext.cs`) ;
- la **transmission** appelle `IPaClient.SendDocumentAsync(PivotDocumentDto document, …)`
  avec le pivot enrichi (`src/Modules/Transmission/Contracts/IPaClient.cs`).

Or **à l'intake, la plateforme ne persiste que des métadonnées + `payload_hash`** :

- registre `received_documents` → **hash seul**, pas de payload
  (`V004__create_received_documents_table.sql`) ;
- `Document` Detected → en-tête + SIREN/nom + **3 totaux** + `payload_hash` ; le
  `DetectedDocumentMapper` **ignore `pivot.Lines` et leur ventilation TVA**
  (`V002__create_documents_table.sql`, `DetectedDocumentMapper.cs`) ;
- l'événement `DocumentReceivedV1` → `TenantId, DocumentId, SourceReference, PayloadHash,
  ReceivedAtUtc` — **sans payload**.

Le pivot complet ne transite qu'**EN MÉMOIRE** via `IDocumentIntake` (best-effort) et n'est
**persisté nulle part en entier** côté plateforme avant l'émission.

ADR-0012 a désigné l'**agent comme détenteur unique** du contenu (« la réconciliation par statut
garde *l'agent comme tampon* — il détient déjà la donnée source »), avec purge au **terminal**,
terminal défini comme « le `Detected` existe ». Cette construction laisse un trou structurel :

- (a) le `Detected` **ne porte pas** le contenu ;
- (b) le **CHECK** (mapping / validation) est **asynchrone, APRÈS** la création du `Detected`,
  déclenché par l'événement outbox qui ne porte que des identifiants + hash ;
- (c) le **SEND** vers la PA est **asynchrone et dépendant de la disponibilité de la PA**
  (reprises, fenêtres de maintenance — parfois plusieurs jours). L'agent — appliance on-prem en
  HTTPS sortant — **ne peut pas rester en otage des jours** détenant l'unique copie.

→ Au moment où le pipeline doit mapper / valider / transmettre, **le contenu a disparu** :
l'agent a purgé au `Detected`, et la plateforme n'a jamais gardé que l'empreinte. **On a le
code-barres, pas le colis.** Le contenu complet n'apparaît en base que **trop tard** — à
l'émission (`DocumentEvent.payload_snapshot`, alimenté par TRK04) et dans le coffre WORM (ADR-0009),
qui n'archivent que **ce qui a été transmis**, après coup ; aucun de ces stockages n'existe **avant**
la transmission.

Contradiction structurelle révélée par l'élaboration de PIP01 : **on ne transmet pas ce que la
plateforme ne détient pas.** L'item a été bloqué et remonté à l'opérateur (CLAUDE.md n°2 :
« si la spec ne tranche pas, bloquer, ne pas deviner »). Latent aujourd'hui (ni le transport AGT
ni le pipeline PIP01 ne sont construits, aucun document réel ne circule), **structurel** dès que
ces maillons existeront.

## Décision

La plateforme **stage durablement le pivot complet dès l'intake**, dans un magasin de contenu
**dédié et transitoire** :

1. **Magasin de staging** — abstraction à capacités `IPayloadStagingStore` (V1 = FileSystem ;
   S3-compatible en fast-follow, comme le coffre d'archive), **NON-WORM / purgeable**. Stocke le
   `PivotDocumentDto` sérialisé (sérialisation canonique ADR-0007), **chiffré au repos**,
   **tenant-scopé**, clé `(tenant_id, source_reference, payload_hash)` / `document_id`.
2. **À l'intake** (`IngestDocumentBatchHandler`) : **écrire ET flusher le blob de staging AVANT**
   de commiter l'inscription au registre + l'événement outbox `DocumentReceivedV1`. C'est un
   **invariant d'ORDRE, pas d'atomicité** — il n'existe pas de transaction distribuée (XA/2PC)
   entre un blob store et Postgres. Ainsi un `DocumentReceivedV1` n'est **jamais** publié sans que
   le contenu soit déjà stagé. Un crash entre l'écriture du blob et le commit laisse au pire un
   **blob orphelin** (purgeable, ré-écrit idempotemment au renvoi de l'agent) — jamais un événement
   sans contenu. Le **contenu n'est plus jeté** ; seul le rangement `Detected` reste best-effort
   post-commit (inchangé). Résout aussi l'« irreconstructibilité » notée par ADR-0012.
3. **Au traitement** (CHECK / SEND) : le pipeline **LIT le pivot depuis le magasin de staging**
   (clé portée par `DocumentReceivedV1` : `document_id` + `payload_hash`). Il **re-vérifie le
   `payload_hash`** à la lecture (intégrité : toute altération est détectée par l'empreinte
   existante — pas besoin de WORM sur le staging). Une entrée de staging **absente** est traitée
   comme « pas encore stagé / retry » (transitoire), **jamais comme terminal/perdu**.
4. **Purge** : l'entrée de staging n'est purgée **que lorsque le contenu est PROUVÉ préservé
   ailleurs** — concrètement, **quand le paquet d'archive WORM existe effectivement**
   (`IArchiveStore.ExistsAsync`), **et non sur la simple étiquette d'état `Issued`** : la transition
   `Issued` puis l'écriture WORM (TRK05) ne sont **pas atomiques**, purger sur l'état seul
   détruirait le contenu **avant** qu'il soit dans le coffre (et l'agent a déjà purgé son filet).
   **Conservée** pour tous les autres états — `Detected / ReadyToSend / Blocked / Sending /
   TechnicalError` **ET `RejectedByPa`** (refusé par la PA : contenu encore nécessaire à la
   correction / resoumission, et **pas** archivé en WORM par le SEND). Un document définitivement
   **abandonné par l'opérateur** (annulation explicite) est purgé par cette **action délibérée**,
   jamais par un balayage d'état. Cette purge est **légitime** : magasin **transitoire de
   traitement**, ce n'est NI une table d'audit NI le coffre WORM (CLAUDE.md n°4 inchangé).

**Rôle de l'agent (amende ADR-0012)** : la file locale de l'agent redevient un **filet de sécurité**
(*belt-and-suspenders*), plus le détenteur unique. L'acquittement en deux temps + le point de statut
(PIP01) **restent** — mais « terminal / `Processed` » signifie désormais « contenu durablement
**STAGÉ** ET `Detected` entré dans le pipeline » (la plateforme détient réellement le document),
garantie strictement plus forte. L'agent peut purger sans risque dès ce terminal.

### Frontières & conformité

- Magasin accédé via l'abstraction `IPayloadStagingStore`, par **Ingestion** (write) et
  **Pipeline** (read) **via Contracts** — aucun accès cross-module Domain/Infra
  (blueprint §2/§6 ; CLAUDE.md n°6/14). Capacité **distincte** de `IArchiveStore` : le staging
  **doit pouvoir être purgé** ; le coffre légal reste **WORM/immuable**. Aucun `if (store is …)`.
- Donnée fiscale sensible : **chiffrée au repos, tenant-scopée** (CLAUDE.md n°9/10). C'est la
  **même donnée** que `payload_snapshot`, simplement **plus tôt et transitoire** — pas une nouvelle
  catégorie de sensibilité.
- Montants : le pivot stocké conserve les `decimal` (ADR-0007) — **aucun float/double** (n°1).
- Lecture seule stricte de la base source (n°5) : inchangée (tout est côté plateforme).
- Intégrité : re-vérification du `payload_hash` existant à la lecture. L'intégrité **légale**
  (hashes chaînés + ancrage RFC3161, ADR-0009/0011) reste sur le **coffre WORM** pour le document
  transmis — **non dupliquée** sur le staging.

## Conséquences

- **PIP00 (nouvel item, segment `pipeline`, AVANT PIP01)** : abstraction `IPayloadStagingStore`
  + impl V1 (FileSystem ; S3-compatible fast-follow) ; l'intake persiste le pivot complet
  (**invariant d'ordre** : blob écrit+flushé avant le commit registre+outbox) ; purge
  **subordonnée à la présence du paquet WORM**. Tests : round-trip chiffré, intégrité hash à la
  lecture, **fenêtre de crash intake**, purge **seulement après WORM confirmé**, **conservation à
  `Detected` et `RejectedByPa`**, isolation entre 2 tenants.
- **PIP01** : CHECK / SEND **lisent le pivot depuis le staging** (fin de la supposition implicite
  « le contenu est déjà là ») ; le « terminal / `Processed` » du point de statut est déterminé par
  l'**état durable du `Document`** (`Detected` et au-delà, **`Issued` inclus**) — la garantie
  « contenu stagé » est un invariant *happened-before* (PIP00 a écrit le contenu **avant**
  l'événement), **non** un contrôle de présence vive du staging (purgé à `Issued`). Un document
  déjà `Issued` répond donc **toujours `Processed`, jamais `Pending`**.
- **AGT (agent)** : mécanisme inchangé (deux temps + statut), **reclassé « filet de sécurité »** —
  peut purger au terminal sans être l'unique détenteur. **Allège** la criticité de la dépendance
  inter-segment notée à GATE_AGENT (l'en-tête de `orchestration/items/PIP.yaml`).
- **Aucune dépendance d'infrastructure nouvelle** hors un magasin de blobs — déjà présent pour
  l'archive (même abstraction de capacités, avec la capacité **purge** en plus).

## Alternatives écartées

- **Garder l'agent comme détenteur unique** (descendre le « terminal » après transmission réussie).
  Couple la transmission — dépendante de la PA, parfois différée de plusieurs jours — à la
  disponibilité d'un agent on-prem en HTTPS sortant ; un agent éteint **bloquerait** la transmission.
  Non retenu : fragile, et l'agent n'a pas à attendre un process asynchrone long (déjà acté ADR-0012).
- **Enrichir `DocumentReceivedV1` du payload complet** (déjà écartée par ADR-0012). Duplique de la
  donnée fiscale complète dans l'**outbox / journal d'événements** (rétention, volume, sensibilité
  dans le log) et détourne le bus d'événements en magasin de contenu. Le magasin de staging dédié,
  **purgeable et chiffré**, est plus propre et borne le volume.
- **Stocker le pivot complet dans la table `documents` du tenant.** Gonfle la base relationnelle du
  tenant avec des payloads volumineux — même raison qu'ADR-0009 pour le coffre (blob hors base,
  référence en base). Non retenu.
