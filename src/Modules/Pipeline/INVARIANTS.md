# Pipeline Module — Invariants

| ID | Rule | Enforcement |
|---|---|---|
| INV-PIPELINE-001 | Le lecteur canonique est le MIROIR EXACT du writer du contrat : `Serialize(Read(json)) == json` octet par octet, échelle `decimal` préservée, énumérations par nom, dates `yyyy-MM-dd`, optionnels omis, collections toujours présentes | `PivotCanonicalJsonReader` (parse du texte brut des décimaux, `Enum.Parse` par nom) — testé `PivotCanonicalJsonReaderTests` (round-trip sur les 8 golden de document unique) |
| INV-PIPELINE-002 | Le lecteur NE RE-SÉRIALISE PAS pour ré-hacher : la re-vérification du `payload_hash` est faite par `IPayloadStagingStore.ReadAsync` sur la string brute (PIP00) | `PivotCanonicalJsonReader.Read` (pure désérialisation, sans hash) ; doc de classe |
| INV-PIPELINE-003 | Aucun comportement de pipeline en PIP01a (pas de CHECK/SEND/SYNC), aucune règle fiscale inventée (catégorie TVA / VATEX / part dérivée) | Revue (P1, CLAUDE.md n°2) ; `ITvaMappingService` prend des requêtes EXPLICITES, ne dérive jamais le `MappingPart` |
| INV-PIPELINE-004 | Le module n'accède aux autres modules que par leurs `Contracts` (jamais `Domain`/`Application`/`Infrastructure`), et ne référence aucune PA concrète | Références de projet (`Infrastructure.csproj`) ; module-rules §3 ; revue (P1, CLAUDE.md n°14) |
| INV-PIPELINE-005 | Le journal d'exécutions `pipeline.run_logs` est TENANT-SCOPÉ par la connexion (database-per-tenant) ; aucune lecture cross-tenant | `PostgresPipelineRunQueries` (via `IConnectionFactory`) ; CLAUDE.md n°9/17 |
| INV-PIPELINE-006 | Une exécution (`RunLog`) ne peut pas se clôturer avant son début, ni porter un compteur négatif | `RunLog.Complete` (gardes `completedAt >= StartedAt`, compteurs ≥ 0) — testé `RunLogTests` |
| INV-PIPELINE-007 | `run_type` / `run_trigger` sont persistés par NOM d'énumération et relus tels quels (fidélité base ↔ contrat) | `PostgresPipelineRunQueries.MapRun` (`Enum.Parse`) — testé `PipelineRunLogQueriesIntegrationTests` |
