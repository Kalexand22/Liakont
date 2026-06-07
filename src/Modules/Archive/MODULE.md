# Module Archive

> Coffre d'archivage fiscal WORM 10 ans (F06 ; items TRK05/TRK06). Lot TRK05 : coffre write-once,
> chaîne de hashes par tenant, addenda chaînés, rendu lisible autonome, vérification d'intégrité,
> store enfichable à capacités. L'ancrage temporel, l'`ArchiveVerifier` complet et l'export contrôle
> fiscal arrivent avec TRK06.

## Purpose

Pour chaque document ÉMIS, constituer un **paquet d'archive immuable** (payload transmis, réponse PA,
rendu lisible, facture PA et bordereau source quand les capacités le permettent, manifest d'empreintes)
et le sceller dans une **chaîne de hashes tamper-evident par tenant**. L'intégrité est **produit**
(SHA-256 chaînés + addenda chaînés) et **indépendante du backend de stockage** : un verrou natif (S3
Object Lock) est utilisé EN PLUS quand il est déclaré, jamais à la place (blueprint §6 ; CLAUDE.md n°4).

## Boundaries

| Schéma / ressource | Accès | Détail |
|---|---|---|
| `documents.archive_entries` | **read + insert** | Table créée par le module Documents (TRK01, migration `V005`) et explicitement « alimentée par TRK05 ». Le module Archive y insère une ligne par paquet ET par addendum, et la lit pour vérifier la chaîne. WORM en base (triggers anti UPDATE/DELETE/TRUNCATE de `V005`). **Aucune migration n'est portée par ce module.** |
| Coffre `IArchiveStore` | **write-once + read** | Backend de stockage abstrait (FileSystem par défaut ; S3-compatible en option ; Azure/GCS fast-follow). Le module ne référence JAMAIS un backend concret. |

Le module **n'accède aux autres modules QUE par leurs `Contracts`** (frontière inter-modules, CLAUDE.md
n°14) et **en lecture seule** : l'export contrôle fiscal et la réversibilité (API03/TRK06) lisent les
documents et leur piste d'audit (`Documents.Contracts`), le paramétrage (`TenantSettings.Contracts`,
`TvaMapping.Contracts`) et le journal opérateur (`Audit.Contracts`). Il ne dépend du code d'aucun autre
module ni d'aucun plug-in PA. Il est **tenant-scopé** : la base route vers le tenant courant
(`IConnectionFactory`), le coffre est rooté sur le tenant courant (`ITenantContext`).

## Endpoints (console, API03)

Montés sous `/api/v1` par le Host (`ArchiveEndpointMapping.MapArchiveEndpoints`), tenant-scopés, sans
logique métier (délégation aux services du module), résultats **streamés en ZIP** :

| Endpoint | Permission | Délègue à |
|---|---|---|
| `GET /documents/{id}/audit-export` | `liakont.read` | `IFiscalControlExportService.BuildForDocumentAsync` |
| `GET /audit-export?from=&to=` | `liakont.read` | `IFiscalControlExportService.BuildForRangeAsync` (granularité mensuelle) |
| `GET /tenant-export` | `liakont.settings` | `ITenantReversibilityExportService.BuildAsync` (réversibilité F12 §6.3) |
| `POST /archive/verify` | `liakont.read` | `IArchiveVerifier.VerifyTenantVaultAsync` (rapport JSON ; altération portée par `IsFullyVerified`) |

## Published Events

Aucun en TRK05 (le scellement est synchrone, appelé par le pipeline).

## Consumed Events

Aucun en TRK05. Le port `IArchiveService` est **appelé directement** par :
- le **pipeline** (PIP, segment ultérieur) à l'entrée en état `Issued` d'un document → `ArchiveIssuedDocumentAsync` ;
- la récupération du **tax-report** (TRK06) et la **réconciliation PDF** (TRK07) → `AddAddendumAsync` ;
- l'**export / vérification** (TRK06, API03/WEB04) → `VerifyTenantChainAsync`.

## Dependencies

- `documents.archive_entries` (schéma du module Documents, TRK01) — la table de référence du coffre.
- `Stratum.Common` : `IConnectionFactory` (connexion tenant-scopée), `ITenantContext` (slug de tenant),
  `TransactionScope`, `pg_advisory_xact_lock` (sérialisation des ajouts de chaîne).
- `IArchiveStore` : `FileSystemArchiveStore` (Infrastructure, défaut) ; `S3ArchiveStore`
  (plug-in `Stores.S3`, AWSSDK.S3, ADR-0009) ; le choix est une **configuration d'instance**.

## Layers

- **Contracts** : `IArchiveService` + DTOs (`ArchivePackageRequest`, `ArchiveAddendumRequest`,
  `ArchiveReadableDocument`, `ArchivePackageResult`, `ArchiveIntegrityReport`…).
- **Domain** : `IArchiveStore` + `ArchiveStoreCapabilities`, `HashChain`, `PackageHasher`,
  `Sha256Hex`, `ArchivePackageLayout`, exceptions WORM.
- **Application** : `ArchiveService`, `ArchivePackageBuilder`, `ReadableDocumentRenderer`,
  `IArchiveEntryStore` (port de persistance + chaînage).
- **Infrastructure** : `FileSystemArchiveStore`, `PostgresArchiveEntryStore` (verrou + chaîne +
  insert dans `documents.archive_entries`), enregistrement DI.
- **Application (TRK06/API03)** : `FiscalControlExportService` (export par document / période / plage),
  `TenantReversibilityExportService` (dossier complet du tenant, secrets masqués).
- **Web (API03)** : `ArchiveEndpointMapping` (endpoints console ci-dessus, ZIP streamé).
- **Stores.S3** (plug-in) : `S3ArchiveStore`, `IS3BlobClient`/`AwsS3BlobClient`, enregistrement opt-in.
