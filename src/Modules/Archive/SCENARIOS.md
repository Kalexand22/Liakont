# Scénarios de test — module Archive

## Unit (`Liakont.Modules.Archive.Tests.Unit`)

### Empreintes et chaîne (INV-ARCHIVE-002, 004)
- `Sha256HexTests` — vecteurs connus SHA-256 (`""`, `"abc"`), hex minuscule 64 caractères, cohérence octets/chaîne.
- `HashChainTests` — genèse sur précédent vide, chaînage `prev+entry`, sensibilité à l'ordre, divergence sur entrée altérée, rejet d'entrée vide.
- `PackageHasherTests` — indépendance de l'ordre, divergence si une empreinte de fichier change, divergence si un NOM change, rejet d'un paquet vide.

### Layout et chemins (INV-ARCHIVE-008)
- `ArchivePackageLayoutTests` — convention `{année}/{mois}/{numéro}/`, assainissement du numéro, rejet de mois invalide, anti path-traversal (`../../etc/passwd` → `passwd`), nom d'addendum séquencé, `Combine`.

### Composition du paquet (INV-ARCHIVE-004, 008, 009)
- `ArchivePackageBuilderTests` — 3 fichiers obligatoires + pièces optionnelles présentes ; absence tracée avec motif ; manifest scellé (entryKind, packageHash, chainHash, files, absentPieces, mappingTrace JSON réelle, notice « pas de certification NF Z42-013 ») ; empreinte d'addendum basée sur le contenu (indépendante du nom).
- `ReadableDocumentRendererTests` — en-tête/lignes/totaux présents, montants en euros, date française ; échappement anti-injection (`<script>` neutralisé) ; accents UTF-8 préservés ; acheteur absent → « Non identifié (B2C) ».

### Stores (INV-ARCHIVE-001, 006)
- `ArchiveStoreContractTests` — l'abstraction `IArchiveStore` n'expose AUCUNE méthode de mutation/suppression (garde structurelle WORM) ; surface limitée à Write/Exists/Read.
- `FileSystemArchiveStoreTests` — round-trip ; idempotence d'un contenu identique ; conflit WORM sur contenu différent ; lecture d'un objet absent → `ArchiveObjectNotFoundException` ; anti path-traversal ; capacités `None`.
- `S3ArchiveStoreTests` — mapping clé (tenant/chemin) ; Object Lock appliqué SSI la capacité est déclarée ; conflit WORM ; lecture absente → introuvable.

### Service (INV-ARCHIVE-002, 003, 007, 008)
- `ArchiveServiceTests` — création de paquet (6 fichiers de contenu dont `archive-metadata.json` + manifest), scellement de l'entrée chaînée ; tenant non résolu → exception ; pièce absente sans motif → exception ; addendum chaîné sur le paquet (chemin dérivé du hash de contenu) ; vérification intacte d'une chaîne honnête ; **détection d'altération de contenu + cascade** sur l'entrée suivante ; **détection d'altération des métadonnées d'audit** (`archive-metadata.json`) ; détection d'altération d'addendum ; détection de pièce manquante.

## Integration (`Liakont.Modules.Archive.Tests.Integration`, PostgreSQL réel + FileSystem réel)

Chaque test tourne sur sa PROPRE base (la table `documents.archive_entries` est WORM — aucun nettoyage
possible entre tests ; la chaîne est globale au tenant).

- `ArchiveIssuedDocument_PersistsEntry_InDocumentsArchiveEntries` — la ligne est écrite dans `documents.archive_entries` (document_id, package_path `…/manifest.json`, package_hash, chain_hash). (INV-ARCHIVE-002, 005)
- `ArchiveIssuedDocument_IsIdempotent_OnReplay` — rejouer la même opération est un no-op : même `EntryId`/`chain_hash`, aucune exception WORM, une seule ligne en base. Garde la régression d'idempotence (précision µs de l'horodatage) invisible au double de test. (INV-ARCHIVE-005)
- `PackageThenAddendum_ChainsAndVerifiesIntact` — paquet puis addendum chaîné ; `VerifyTenantChainAsync` intact, 2 entrées. (INV-ARCHIVE-003)
- `ArchiveEntry_Update_IsRejectedByWormTrigger` / `ArchiveEntry_Delete_IsRejectedByWormTrigger` — la garde WORM base (triggers V005) rejette tout UPDATE/DELETE (`PostgresException` « WORM »). (INV-ARCHIVE-001)
- `VerifyTenantChain_DetectsFileAlteration_OnRealStore` — altération directe sur disque d'une pièce → rapport non intact, contenu invalide. (INV-ARCHIVE-002)
- `ArchivedUtc_IsStrictlyIncreasing_AcrossEntries` — l'horodatage d'archivage est strictement croissant (ordonnancement déterministe de la chaîne). (INV-ARCHIVE-005)

## Hors CI (staging — blueprint §9)

Le tour réel sur un backend S3-compatible (Amazon/MinIO/OVH/Scaleway) avec Object Lock natif relève d'un
test de staging manuel : `AwsS3BlobClient` n'est pas exercé en CI (seule la logique `S3ArchiveStore` l'est,
via un double de `IS3BlobClient`).
