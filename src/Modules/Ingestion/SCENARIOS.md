# Ingestion Module — Test Scenarios

## Unit Tests (Tests.Unit/)

### AgentTests
- Création : génère un `key_prefix` et un `key_hash`, renvoie la clé complète au format `prefix.secret` ; la clé en clair n'est jamais portée par l'entité — INV-INGESTION-001
- L'empreinte stockée correspond à la clé émise, et seulement à elle — INV-INGESTION-001
- `RotateKey` change préfixe + empreinte et renvoie une nouvelle clé ; l'ancienne ne correspond plus — INV-INGESTION-001
- `RotateKey` sur un agent révoqué lève `ConflictException` — INV-INGESTION-008
- `Revoke` est idempotent et pose `revoked_at`
- `RecordHeartbeat` met à jour la dernière vue et la version d'agent
- Nom vide / tenant vide rejetés à la création

### AgentKeyTests
- `HashesMatch` vrai pour la bonne clé, faux sinon (comparaison en temps constant) — INV-INGESTION-001
- `TryExtractPrefix` extrait `prefix` de `prefix.secret` ; rejette une clé nulle/vide/sans séparateur

### AgentContractVersionPolicyTests
- Version courante « 1 » supportée ; `null`, vide, « 0 », « 2 », valeur inconnue NON supportées (→ 426) — INV-INGESTION-005

### DocumentIngestionDecisionTests (anti-doublon pur, PIV04)
- Empreinte de payload déjà connue → `Duplicate`, jamais accepté — INV-INGESTION-009
- Re-push du même payload (réf + empreinte identiques) → `Duplicate`, jamais altération — INV-INGESTION-009
- Référence source inconnue → `AcceptedNew` — INV-INGESTION-009
- Référence source connue avec empreinte différente → `AcceptedAltered` + `PreviousPayloadHash` — INV-INGESTION-010
- Empreinte vide rejetée (argument invalide)

## Integration Tests (Tests.Integration/ — PostgreSQL Testcontainers)

### AgentLifecycleIntegrationTests
- Enregistrement : la clé complète n'est renvoyée qu'à l'émission ; en base, `key_prefix` + `key_hash` sont présents et NE contiennent PAS le clair — INV-INGESTION-001
- Authentification d'une clé valide → identité du bon agent et du bon tenant — INV-INGESTION-002
- Authentification d'une clé inconnue/mal formée → `InvalidKey` (401) — INV-INGESTION-004
- Authentification d'une clé révoquée → `Revoked` (403) — INV-INGESTION-004
- Rotation : l'ancienne clé devient invalide, la nouvelle s'authentifie — INV-INGESTION-001
- Révocation : la clé est ensuite refusée (`Revoked`)
- Isolation tenant : la liste d'un tenant ne voit pas les agents d'un autre ; révoquer l'agent d'un autre tenant échoue (`NotFoundException`) — INV-INGESTION-002
- La liste d'agents n'expose jamais la clé (préfixe seul) — INV-INGESTION-001

### HeartbeatIntegrationTests
- Un heartbeat est persisté (ligne append-only) et met à jour `last_seen_at` + `last_agent_version` de l'agent — INV-INGESTION-006
- La réponse renvoie l'heure serveur et une configuration au défaut sûr (`updateRequired = false`, champs d'update et `extractionSchedule` à `null`) — INV-INGESTION-007

### AgentConfigurationIntegrationTests
- `GetAgentConfiguration` renvoie le défaut sûr tant que le registre de versions est vide — INV-INGESTION-007

### DocumentBatchIngestionIntegrationTests (PIV04 — PostgreSQL Testcontainers)
- Lot nominal : chaque document inédit → `Accepted`, ligne dans `received_documents`, événement `ingestion.document.received` dans l'outbox — INV-INGESTION-009, INV-INGESTION-011
- Doublon : re-pousser le même payload → `Duplicate`, aucune nouvelle ligne, aucun nouvel événement — INV-INGESTION-009
- Re-push complet après réinstallation d'agent (tout le lot re-poussé) → tout en `Duplicate` — INV-INGESTION-009
- Altération : même `source_reference`, payload différent → `Accepted` + événement `ingestion.source.altered` (avec `PreviousPayloadHash`) ET `ingestion.document.received` — INV-INGESTION-010
- Lot mixte (nouveau + doublon + malformé) → résultats individuels respectifs, le malformé n'affecte pas les autres — INV-INGESTION-013
- Document malformé (référence/numéro manquant) → `Rejected` avec motif, aucune écriture — INV-INGESTION-013
- Isolation tenant : un payload reçu pour le tenant A n'est pas un doublon pour le tenant B (chaque tenant a son propre anti-doublon) — INV-INGESTION-012
- Régimes de TVA source du push persistés par tenant ; occurrences = dernière observation (remplacée, non cumulée → idempotent au retry), lisibles via `ISourceTaxRegimeQueries` — INV-INGESTION-015

### IngestedPdfStoreTests (PIV04)
- PDF rattaché écrit sous `{tenant}/linked/{sha256(sourceReference)}.pdf` ; re-push écrase — INV-INGESTION-014
- PDF de pool écrit sous `{tenant}/pool/` avec préfixe GUID (dépôts distincts, pas d'écrasement) — INV-INGESTION-014
- Tenants distincts → arborescences distinctes ; nom de fichier assaini (anti path-traversal) — INV-INGESTION-014
