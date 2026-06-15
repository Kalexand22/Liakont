# TODO — Démo extraction réelle bout-en-bout : 2 bases SQL + 2 adaptateurs + AGT02 + install multi-services

> Objectif (interactif, human-driven) : prouver l'extraction réelle d'un agent installé.
> Deux bases SQL Server de schémas différents → deux adaptateurs ODBC → le service agent
> extrait et pousse les factures vers la plateforme locale, qui les fait remonter en console.
> Qualité produit (branche dédiée, tests, ADR, codex-review). Fait avancer **AGT02**.
> Branche : `feat/agt02-demo-extraction` (depuis origin/main). ADR : ADR-0023.

## Décisions de conception (voir ADR-0023)

- D1 tenant unique `default` (SIREN 123456782, mapping {20,10,5.5,0}→{S,AA,AA,Z}, PA Fake Staging).
- D2 canal `adapterConfig.<nom>` dans agent.json (loader générique, fabrique valide sa section).
- D3 câblage AgentRunCycle au composition root du service (remplace le cycle vide).
- D4 DemoErpA (decimal, normalisé, FAC/AVO) + DemoErpB (float legacy, dénormalisé, I/C).
- D5 emitterSiren/operationCategory en adapterConfig (≠ source).
- D6 données fictives sous deployments/demo-local/ (règle n°7).
- D7 cas connus : simple(20) · multi-mixte(20+10+5.5) · taux 0 · avoir → facture · 1 régime NON mappé → Blocked.
- D8 numéros auto re-jouables ; SourceReference namespacé par adaptateur.
- D9 adaptateurs démo embarqués, marqués démo.
- D10 login SQL dédié lecture seule (db_datareader), ODBC Driver 17, UID/PWD chiffré DPAPI.

## Phases

### Phase 0 — Cadrage
- [x] Branche `feat/agt02-demo-extraction` depuis origin/main
- [x] ADR-0023 (câblage run cycle + canal adapterConfig)

### Phase 1 — Infra SQL de démo (aucun code produit) — DONE
- [x] deployments/demo-local/01-create-databases.sql (2 bases + login ro)
- [x] 02-schema-erpA.sql / 03-schema-erpB.sql
- [x] 04-seed-erpA.sql / 05-seed-erpB.sql (re-jouables, numéros auto, cas connus)
- [x] seed-demo.ps1 (wrapper sqlcmd) + README.md
- [x] Gate : test-odbc connecte les 2 bases (ErpA 6 fact/8 lignes, ErpB 6 inv/8 items ; écriture refusée)

### Phase 2 — Canal adapterConfig (Core) — DONE
- [x] AgentConfigLoader + DTO : parse adapterConfig (générique) + AdapterConfigSection + AgentConfig.GetAdapterConfig
- [x] Tests unitaires Core (19/19, dont 4 adapterConfig)
- [x] Gate : verify-fast vert (plateforme + agent x86/x64) — fix CA1859 (type retour concret)

### Phase 3 — Deux adaptateurs ODBC (IExtractor) — DONE
- [x] Helpers Core réutilisables : SourceAmounts (float→decimal), OdbcCellReader, SourceQuery, SourceQueryGuard, OdbcSourceConnectionFactory, SourceEmitterConfig
- [x] Liakont.Agent.Adapters.DemoErpA (decimal, normalisé) + Liakont.Agent.Adapters.DemoErpB (float, dénormalisé)
- [x] Tests unitaires mappers (5+4) + SourceAmounts/SourceEmitterConfig/SourceQueryGuard ; frontière étendue (AgentBoundaryTests)
- [x] Intégration ODBC : convention agent = tests unitaires only (pas de vraie base en CI) → validation LIVE en Phase 6
- [x] Gate : x86+x64 clean, Core 253/253, mappers verts

### Phase 4 — Câblage du cycle de run (AGT02) — DONE
- [x] EmbeddedSourceAdapters étendu (noms + CreateConfigured DemoErpA/B) + extractFromUtc (config+AgentRunCycle)
- [x] AgentRunComposition (Cli/Hosting) partagé service ⇄ CLI run ; AgentHost compose le vrai cycle (repli neutre si config invalide)
- [x] RunCommand câblé sur la composition réelle ; service référence le CLI
- [x] Tests : extractFromUtc (cycle + config) + tous existants ; Gate verify-fast VERT (plateforme + agent x86/x64)

### Phase 5 — Agents + install des 2 services — DONE
- [x] 2 clés API émises (POST /api/v1/agents via token parametrage ; directAccessGrants Keycloak activé puis RÉVERTÉ ; pièges : reset mdp + purge requiredActions TOTP + purge brute-force)
- [x] agent.json des 2 instances (C:\ProgramData\Liakont\<instance>\) : secrets DPAPI + adapterConfig + extractFromUtc
- [x] check-config + test-odbc + test-api (clé réelle ACCEPTÉE) OK
- [x] Script install-services.ps1 prêt (vrais services Windows — session non élevée → clé en main ; extraction prouvée via CLI run = MÊME composition)

### Phase 6 — Vérif bout-en-bout + review
- [x] CLI run ×2 : 6 docs/instance extraits+poussés ; réconciliation 2-temps (6 acquittés) ; idempotence (re-run 0)
- [x] Plateforme : 12 documents, 8 ReadyToSend + 4 Blocked (avoirs + régime 13) — cas connus confirmés
- [x] Correctif bug ODBC 22008 (DateTime infra-seconde → troncature seconde) + test
- [x] codex-review : round 3 CLEAN (rounds 1+2 = 9 P2 corrigés, 0 P1 sur 3 rounds)
- [x] Section Review (ci-dessous)

## Vigilance
- Pas de categoryCode/vatexCode côté agent (P1). decimal partout (P1). Lecture seule stricte (P1).
- Plateforme cible = Host clone voisin Liakont4 (localhost:55996) — ne pas perturber.
- Service en LocalSystem : login SQL dédié évite la dépendance d'identité.

## Review

### codex round 1 — 0 P1, 5 P2 (tous corrigés)
- **P2-1** (crash-loop SCM) : `AgentRunComposition.Build` traduit les échecs de déchiffrement DPAPI
  (`Cryptographic/FormatException`, agent.json d'une autre machine) en `AgentConfigException` → le
  service/CLI bascule en cycle neutre journalisé au lieu de planter.
- **P2-2** (stall sur 1 doc malformé) : `DemoErp*Extractor.TryMapDocument` met en QUARANTAINE (journalisé)
  un document à `SourceSchemaException` et poursuit la fenêtre (le filigrane avance). Journal cascadé
  extracteur ← fabrique ← `CreateConfigured` ← `Build`. Testé.
- **P2-3** (verrou de run non partagé) : `AgentHost` fait acquérir au cycle du service le MÊME
  `InterProcessRunLock` (mutex par instance) que la commande CLI `run` ; verrou détenu → cycle sauté.
- **P2-4** (regroupement streaming non testé) : `DemoErpAExtractorTests` éprouve `ExtractDocuments` avec
  un `IDataReader` factice (multi-lignes, facture sans ligne, avoir+origine, quarantaine).
- **P2-5** (faux-vert `sc start`) : `install-services.ps1` vérifie `$LASTEXITCODE` de `install`/`sc start`
  + attend l'état `Running`.
- Écarté par le reviewer (pas un P1) : les `float` de DemoErpB sont le reflet brut d'une base legacy,
  convertis decimal half-up à la frontière (`SourceAmounts`/`PivotRounding`) — aucun calcul flottant.

### codex round 2 — 0 P1, 4 P2 (tous corrigés)
- **AgentHost** : capture désormais TOUT échec de composition (pas que `AgentConfigException` — file SQLite
  corrompue, URL invalide…) → cycle neutre journalisé (anti crash-loop). + fuite httpClient colmatée dans `Build`.
- **DemoErpBExtractorTests** ajouté (miroir de A) ; fakes ADO extraits dans `agent/tests/_shared/FakeAdoStack.cs`
  (lié dans les 2 projets, non dupliqué).
- **Schémas démo** : contrainte UNIQUE sur `numero`/`InvoiceNo` (l'auto-jointure d'origine suppose l'unicité).
- **install-services.ps1** : `Write-Error`→`Write-Warning` (sous `ErrorActionPreference=Stop`, Write-Error
  était terminant → avortait tout) + code 1056 (service déjà lancé) toléré.

### État final
- verify-fast vert (plateforme + agent x86/x64, tous tests). Re-validation live : 24 documents avec binaires corrigés.
- Bout-en-bout prouvé : depuis 2 bases SQL / 2 adaptateurs → 16 ReadyToSend + 8 Blocked (cas connus).
- **Reste à l'humain** : revue + merge (le commit/merge n'est pas fait par l'agent). Vrais services
  Windows : `deployments/demo-local/install-services.ps1` (console élevée).
