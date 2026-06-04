# ADR-0009 — Stockage du coffre d'archive WORM : FileSystem + S3-compatible (TRK05)

**Date :** 2026-06-04

**Statut :** Accepté (2026-06-04)

---

## Contexte

Le module `Archive` (TRK05) constitue le coffre d'archivage fiscal 10 ans (F06 ; offre commerciale
« archivage 10 ans inclus »). Le stockage du coffre est le **3ᵉ axe de généricité enfichable**
(blueprint.md §2 règle 6, §6 ; F12 §7 décision D9 ; module-rules.md §5) : l'éditeur choisit son backend
au niveau **instance**, sans que le module ne connaisse de backend concret (`if (store is S3)` interdit —
P1, CLAUDE.md n°14).

Quatre contraintes encadrent ce choix :

1. **Intégrité PRODUIT, indépendante du backend.** La preuve d'intégrité est la chaîne de hashes SHA-256
   (+ addenda chaînés + ancrage temporel TRK06), calculée par le produit. Elle ne dépend JAMAIS du WORM
   natif du backend. Un verrou matériel, quand le backend l'offre, est une protection **supplémentaire**
   (ceinture + bretelles), jamais la garantie de référence.
2. **Deux topologies V1.** L'appliance self-hosted écrit sur un **volume local** ; les instances hébergées
   visent un **objet S3-compatible**. Les deux doivent marcher avec le MÊME module et le MÊME comportement
   métier.
3. **Un seul code pour tout S3-compatible.** Amazon S3, MinIO, OVH, Scaleway, Wasabi partagent l'API S3 ;
   un unique plug-in les couvre (endpoint + identifiants = configuration d'instance).
4. **Aucune donnée client ni secret dans le code** (CLAUDE.md n°7, n°10) : bucket, endpoint, clés sont du
   paramétrage d'instance, jamais versionnés.

## Décision

Le coffre est consommé derrière l'abstraction **`IArchiveStore`** (couche `Domain` du module), à
**capacités déclarées** (`ArchiveStoreCapabilities` : `SupportsObjectLock`, `SupportsLegalHold`).
L'interface est **write-once par construction** : `WriteAsync` / `ExistsAsync` / `ReadAsync`, aucune
méthode de modification ni de suppression (WORM dans la forme même de l'abstraction — CLAUDE.md n°4).

Deux implémentations V1 :

- **`FileSystemArchiveStore`** (Infrastructure, store par DÉFAUT, appliance) : un répertoire par tenant
  sous une racine d'INSTANCE (`Archive:Storage:FileSystem:RootPath` ; repli sous le répertoire de
  l'instance si non configuré). Écriture `CreateNew` + passage en lecture seule ; idempotente pour un
  contenu identique, `ArchiveWriteConflictException` sur réécriture d'un contenu différent. Capacités :
  `None` — l'intégrité repose entièrement sur la chaîne de hashes.
- **`S3ArchiveStore`** (plug-in `Liakont.Modules.Archive.Stores.S3`, opt-in) : un seul code pour tout
  backend S3-compatible. Quand la capacité `SupportsObjectLock` est déclarée, chaque objet est écrit avec
  **Object Lock en mode conformité** pour la durée de rétention fiscale (10 ans, art. L.123-22), EN PLUS
  de la chaîne de hashes. Le `applyObjectLock` est piloté par la **capacité**, jamais par un test de type.

**Dépendance : `AWSSDK.S3` (4.0.24)**, isolée dans le SEUL projet `Stores.S3` (le Host par défaut ne la
référence pas — il reste sur FileSystem). Le SDK est encapsulé derrière la couture `IS3BlobClient`
(`AwsS3BlobClient`), ce qui rend `S3ArchiveStore` testable sans backend réel.

Le choix du backend est une **configuration d'instance** : `AddArchiveModule` enregistre FileSystem par
défaut ; une instance qui choisit S3 appelle `AddS3ArchiveStore` (qui remplace l'enregistrement). Aucun
flag produit, aucun `if` sur un backend.

## Conséquences

- **Généricité respectée.** Le module ne voit que `IArchiveStore` + capacités. Azure Blob et GCS sont des
  **plug-ins fast-follow** (mêmes capacités, nouveau projet, aucun changement du module).
- **Intégrité garantie partout.** FileSystem sans verrou natif est aussi sûr que S3 avec Object Lock du
  point de vue de la DÉTECTION d'altération (chaîne de hashes) ; l'Object Lock ajoute une PRÉVENTION
  matérielle quand il est disponible.
- **Tests.** `FileSystemArchiveStore` est testé en intégration (FS réel) ; `S3ArchiveStore` est testé
  unitairement contre un double de `IS3BlobClient` (mapping de clé, WORM, pilotage de l'Object Lock par la
  capacité). Le tour réel sur un S3-compatible (MinIO/OVH…) avec Object Lock est un **test de staging**,
  hors CI (blueprint §9) — `AwsS3BlobClient` (le wrapper SDK fin) n'est exercé qu'à ce niveau.
- **OPS.** La racine FileSystem (volume, sauvegarde, rétention) et la configuration S3 (bucket, endpoint,
  Object Lock, identifiants via secret) sont des paramètres d'exploitation par instance — à documenter
  dans le toolkit de déploiement (lot OPS/DOC).
- **Limite assumée.** Ce coffre n'est PAS un SAE certifié NF Z42-013 (mentionné dans le manifest et la
  doc) ; l'argument commercial est « scellement qualifié eIDAS (ancrage RFC 3161, TRK06) + ancrage
  blockchain en option », jamais la certification.

## Alternatives écartées

- **Stocker le coffre en base (bytea/large object)** — gonfle la base du tenant, complique sauvegarde et
  export ; un coffre de pièces binaires volumineuses appelle un store objet/fichier (cohérent avec
  ADR-0008 pour le pool PDF).
- **Coupler l'intégrité au WORM natif du backend** (S3 Object Lock seul) — rendrait l'intégrité dépendante
  du backend et inopérante sur l'appliance FileSystem ; contraire à blueprint §6. La chaîne de hashes
  produit reste la garantie de référence.
- **Un plug-in par fournisseur S3** (Amazon, MinIO, OVH…) — inutile : l'API S3 est commune ; un seul code
  paramétré par endpoint suffit. Azure/GCS, dont l'API diffère, restent des plug-ins distincts.
- **Embarquer AWSSDK dans la couche Infrastructure du module** — imposerait le SDK à toute instance, même
  FileSystem ; la dépendance est isolée dans le plug-in `Stores.S3`.
