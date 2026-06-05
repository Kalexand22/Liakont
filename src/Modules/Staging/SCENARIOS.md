# Staging Module — Scenarios

Scénarios de test couverts, par niveau, référençant les invariants (`INV-STAGING-NNN`). Les tests de
l'invariant d'ORDRE de l'intake (staging écrit avant le commit) vivent côté `Ingestion.Tests.Integration`
(c'est le handler d'intake qui porte l'ordre) et référencent `INV-INGESTION-017`.

## Unit — `FileSystemPayloadStagingStoreTests`

- **Round-trip fidèle** : un pivot canonique écrit puis relu restitue le MÊME JSON, montants `decimal`
  à échelle préservée (INV-STAGING-001).
- **Chiffré au repos** : les octets écrits sur le disque ne contiennent pas le JSON en clair ; la lecture
  déchiffre correctement (INV-STAGING-002).
- **Intégrité — hash mismatch** : lire avec une clé dont le `payload_hash` ne correspond pas au contenu
  → `StagedPayloadIntegrityException` (INV-STAGING-003).
- **Intégrité — blob altéré** : corrompre le fichier chiffré → `StagedPayloadIntegrityException`
  (déchiffrement AEAD échoué), jamais un contenu erroné servi (INV-STAGING-003).
- **Absence transitoire** : lire une clé jamais stagée → `StagedPayloadNotFoundException` (pas terminal)
  (INV-STAGING-004).
- **Isolation tenant** : stagé pour le tenant A ; `ExistsAsync`/`ReadAsync` pour le tenant B (même
  `document_id`) → absent / `StagedPayloadNotFoundException` (INV-STAGING-005).
- **Purge** : `PurgeAsync` supprime l'entrée ; re-purge d'une entrée absente = no-op (INV-STAGING-006).
- **Idempotence d'écriture** : ré-écrire le même contenu sous la même clé → une seule entrée, relue
  intègre (INV-STAGING-008).
- **Capacités** : `Capabilities == None` (chiffrement/purge produit, jamais de `if (store is …)`)
  (INV-STAGING-009).

## Unit — `StagingPurgeServiceTests`

- **Purge subordonnée — WORM présent** : la sonde retourne vrai → l'entrée est purgée, retour `true`
  (INV-STAGING-007).
- **Purge suspendue — WORM absent** : la sonde retourne faux (ex. entre `Issued` et l'écriture TRK05) →
  l'entrée est CONSERVÉE, retour `false`, aucune purge (INV-STAGING-007).

## Integration — `Ingestion.Tests.Integration` (invariant d'ordre de l'intake)

- **Stagé AVANT le commit** : pendant l'écriture du blob, la ligne `received_documents` n'est pas encore
  visible (transaction non committée) ; après le retour, le blob ET l'événement existent
  (INV-INGESTION-017).
- **Échec de staging → rien de committé** : un magasin qui échoue à l'écriture fait remonter l'erreur ;
  aucune ligne `received_documents`, aucun événement `DocumentReceived` (jamais d'événement sans contenu)
  (INV-INGESTION-017).
- **Doublon → pas de re-staging** : un re-push d'un payload déjà reçu reste `duplicate`, sans écriture de
  staging (le contenu de la première réception suffit) (INV-INGESTION-017).
