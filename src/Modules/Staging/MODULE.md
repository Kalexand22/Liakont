# Module Staging

> Magasin durable et TRANSITOIRE du contenu pivot à l'intake (PIP00 ; ADR-0014, amende ADR-0012). La
> plateforme détient le pivot complet pendant tout le traitement (CHECK → SEND), l'agent on-prem
> redevient un FILET DE SÉCURITÉ (plus le détenteur unique). Magasin enfichable à capacités, NON-WORM /
> purgeable, chiffré au repos, tenant-scopé — capacité DISTINCTE du coffre d'archive WORM (`Archive`).

## Purpose

À l'intake, persister DURABLEMENT le pivot **complet** (lignes, ventilation TVA par-ligne, parties)
sérialisé en forme canonique (ADR-0007), pour que le pipeline (PIP01) le RELISE au CHECK et au SEND au
lieu de supposer « le contenu est déjà là ». Le contenu n'est **plus jamais jeté** à l'intake : il est
écrit + flushé **avant** que l'événement `DocumentReceivedV1` ne soit committé (invariant d'ORDRE, pas
d'atomicité). L'entrée est **purgée** uniquement quand le contenu est PROUVÉ préservé ailleurs — le paquet
d'archive WORM effectivement présent (`IArchiveStore.ExistsAsync`) — jamais sur la seule étiquette d'état
`Issued`.

## Boundaries

| Ressource | Accès | Détail |
|---|---|---|
| Magasin `IPayloadStagingStore` | **write + read + exists + purge** | Backend de blob abstrait, NON-WORM / purgeable (FileSystem V1 ; S3-compatible fast-follow). Chiffré au repos (ASP.NET Core Data Protection, protecteur dérivé par tenant), tenant-scopé (un répertoire par tenant). Le module ne référence JAMAIS un backend concret (`if (store is …)` interdit — P1). |
| Coffre WORM (présence) | **exists (indirect)** | Via le port `IArchivedDocumentProbe` (inversion de dépendance) ; l'adaptateur concret vers `IArchiveStore` est câblé au composition root (Host). Le module ne référence PAS le module Archive. |

Le module **n'accède à aucun autre module** hors `Contracts` : `Ingestion` écrit via `IPayloadStagingStore`,
le pipeline lit via la même surface. Aucune migration, aucune table relationnelle (le contenu est un blob
hors base, référencé par `(tenant, document_id)`). Tenant-scopé par la clé `StagedPayloadKey`.

## Published Events

Aucun. Le magasin est appelé **directement** via Contracts (write à l'intake, read/purge au pipeline).

## Consumed Events

Aucun. Le module ne consomme pas d'événements ; il est un magasin de contenu synchrone.

## Dependencies

- `Liakont.Agent.Contracts` : `CanonicalJson` / `PayloadHasher` (sérialisation + empreinte canonique
  ADR-0007), utilitaires BCL-only, aucune logique métier.
- ASP.NET Core Data Protection (framework partagé `Microsoft.AspNetCore.App`) : chiffrement au repos,
  même mécanisme que les secrets PA — aucun package NuGet nouveau, aucun ADR (repo-standards §4).
  **PRÉREQUIS DE DÉPLOIEMENT (OPS)** : un magasin de clés Data Protection PERSISTANT (même exigence que
  les secrets PA — cf. `TenantSettingsModuleRegistration`). Tant qu'il est éphémère, un redémarrage
  d'instance (ou une rotation de clé) rend un blob stagé en cours indéchiffrable
  (`StagedPayloadIntegrityException`). Ce n'est PAS une perte : le contenu reste détenu par le FILET DE
  SÉCURITÉ de l'agent (ADR-0014), qui re-pousse tant que le statut n'est pas `Processed` — le document
  est alors re-stagé puis traité. La façon dont le pipeline (PIP01) réagit à une relecture indéchiffrable
  (re-demander à l'agent vs erreur) relève de PIP01.
- `IArchivedDocumentProbe` : port OWNED par ce module ; implémenté au Host
  (`ArchiveStoreArchivedDocumentProbe`) en sondant `IArchiveStore.ExistsAsync`. La frontière inter-modules
  (Contracts uniquement) reste intacte — le câblage cross-module est le rôle du composition root.

## Layers

- **Contracts** : `IPayloadStagingStore` + `PayloadStagingStoreCapabilities`, `StagedPayloadKey`,
  exceptions (`StagedPayloadNotFoundException` transitoire, `StagedPayloadIntegrityException`),
  `IStagingPurgeService`, `IArchivedDocumentProbe` + `ArchivedDocumentLocator`.
- **Infrastructure** : `FileSystemPayloadStagingStore` (chiffrement au repos, intégrité à la lecture,
  écriture flushée + renommage atomique), `StagingPurgeService` (purge subordonnée WORM),
  `StagingPathLayout` (assainissement anti path-traversal), enregistrement DI.

## Consumers (segments ultérieurs)

- **PIP01** (pipeline) : relit le pivot au CHECK/SEND via `ReadAsync` ; purge après émission via
  `IStagingPurgeService.PurgeIfArchivedAsync` (subordonnée au paquet WORM).
- **Ingestion** (déjà câblé, PIP00) : `IngestDocumentBatchHandler` écrit via `WriteAsync` avant le commit.

## Cycle de vie & dette connue

La purge via `IStagingPurgeService.PurgeIfArchivedAsync` est **subordonnée à la présence WORM** (par design,
ADR-0014 §4) : un document stagé n'est purgé que si son paquet d'archive est effectivement dans le coffre
(`IArchiveStore.ExistsAsync`). Ce comportement est correct pour le chemin nominal (émis → archivé → purgé).

**Cas non couvert :** un document stagé + committé à l'intake qui n'atteint JAMAIS le coffre WORM (rejet PA
terminal, abandon du traitement) ne sera JAMAIS purgé par `PurgeIfArchivedAsync`. En l'absence de politique
de rétention, le staging croît de façon non bornée pour ces documents.

**Sous-cas FIX07b (réhydratation au re-push) :** la réhydratation du staging perdu au re-push de l'agent
(`IngestDocumentBatchHandler.RetryRangingIfNotRangedAsync`) ré-écrit un blob même pour un document déjà
`Issued` dont le staging a été légitimement purgé — uniquement en reprise après PERTE D'ÉTAT de l'agent
(re-scan/re-push d'un document terminal). Sans effet fonctionnel (aucun événement ne subsiste → ni re-range
ni ré-émission ; le document reste `Issued`), mais ce blob ne repassera pas par la purge du SEND : il relève
de la MÊME dette « croissance non bornée » (rare, idempotent, borné par document). Le borner à l'intake
exigerait la connaissance du cycle de vie / présence WORM dans le chemin chaud et risquerait de re-casser la
réhydratation légitime des états pré-émission (Detected/ReadyToSend/Blocked, INV-STAGING-007) — il est donc
laissé à la politique de rétention PIP01 ci-dessous.

**Prérequis PIP01 (propriétaire du cycle de vie) :** définir et appliquer une politique bornant cette
croissance (purge sur échec terminal OU balayage d'expiration borné). La durée de rétention et les critères
d'éligibilité DOIVENT être issus de `docs/conception/` et ne sont PAS définis ici.
