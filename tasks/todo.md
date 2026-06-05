# AGT02 — Contrat IExtractor + run d'extraction + client de push

Branche : `feat/agent-AGT02` (slot-3). Spec : F12 §2.2/§3 + F01-F02 §4 +
ADR-0004 D2 (capacités) + ADR-0012 (ACK 2 temps).
Frontière : `Liakont.Agent.Core` ne référence que BCL + Newtonsoft.Json + System.Data.SQLite
(+ System.Net.Http, BCL, pour le client HTTP). Aucune logique métier.

## Plan

### Contrat (Contracts, add-only — ADR-0012)
- [ ] `DocumentIntakeStatus` (Pending/Processed/Rejected) + `DocumentStatusResultDto`
      (point de statut GET /api/agent/v1/documents/status).

### Contrat d'extraction (Core/Extraction)
- [ ] `IExtractor` complet (GetInfo, CheckHealth, Capabilities, ExtractDocuments,
      ExtractPayments, ListSourceTaxRegimes, GetAttachments, ListPoolDocuments).
- [ ] `ExtractorInfo`, `HealthCheckResult`, `ExtractorCapabilities` (ADR-0004 D2),
      `SourceAttachment`, `PoolDocument`, exceptions `SourceUnavailableException`/`SourceSchemaException`.
- [ ] `FixtureExtractor` générique (rejoue des documents pivot JSON).
- [ ] `EncheresV6Extractor` : placeholder mis à jour pour le contrat étendu (réel = lot ADP).

### Run d'extraction (Core/Extraction)
- [ ] `ExtractionCycle` : EXTRACT (enqueue idempotent, skip déjà-ACKé) → COLLECT PDF selon
      capacités → régimes TVA source stashés → filigrane. `ExtractionWindow` (watermark).

### Transport (Core/Transport)
- [ ] `IPlatformClient` + `HttpPlatformClient` (System.Net.Http) : batch/pdf/pdf-pool/status,
      en-têtes X-Agent-Key/X-Contract-Version, codes F12 §3.3.
- [ ] `ExponentialBackoff` ; `QueueDrainer` (ACK 2 temps ; batching 100 ; re-découpe 413 ;
      backoff 429/5xx/réseau ; pas de retry 400 ; idempotence).
- [ ] `AgentRunCycle` : composition extraction + drain (injectable dans l'hôte AGT01).

### LocalQueue (additions minimales)
- [ ] `MarkPending(id)` + `Peek(status, kind?, max)`.

### Tests (net48, serveur mocké HttpListener)
- [ ] FixtureExtractor, ExtractionCycle, ExponentialBackoff, HttpPlatformClient (tous codes
      F12 §3.3), QueueDrainer (ACK 2 temps, reprise après coupure, idempotence, re-découpe).
- [ ] Contracts : DocumentStatusResultDto.

### Vérification
- [ ] verify-fast (2 solutions) + run-tests + codex-review propre.

## Review
(à compléter en fin d'item)
