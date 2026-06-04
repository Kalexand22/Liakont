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
