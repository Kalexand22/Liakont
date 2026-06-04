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

Le module **n'accède à aucun autre module** : il ne dépend ni du code de `Documents` (seule la table
partagée `documents.archive_entries`, par SQL, sans référence d'assembly), ni d'aucun plug-in PA. Il est
**tenant-scopé** : la base route vers le tenant courant (`IConnectionFactory`), le coffre est rooté sur
le tenant courant (`ITenantContext`).

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
- **Stores.S3** (plug-in) : `S3ArchiveStore`, `IS3BlobClient`/`AwsS3BlobClient`, enregistrement opt-in.
