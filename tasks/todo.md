# FIX07b — Documents au staging perdu : re-stage par re-push agent + emplacement stable

## Contexte (bug-inbox P2, décision D6)
Un document `Detected` dont le contenu stagé (PIP00/ADR-0014) a disparu devient un **zombie
définitif** : le re-push de l'agent (filet de sécurité ADR-0014) est un doublon par empreinte, et
`RetryRangingIfNotRangedAsync` fait un early-return dès que `IsDocumentRangedAsync == true` (le
`Detected` existe) — **sans re-stager**. Le CHECK ne peut alors jamais relire le pivot.

Cause aggravante (récurrence) : le défaut de `Staging:Storage:FileSystem:RootPath` est
`AppContext.BaseDirectory/staging-store` = **sous l'arbre de build** (`bin/`), effacé au
redéploiement/rebuild.

## Décisions / sources
- D6 : re-stage au re-push + emplacement de staging stable. Alerte supervision « en attente de
  contenu » = **fast-follow** (hors périmètre).
- Outbox dead-letter (socle Stratum, **non modifié**) : `StagedPayloadNotFoundException` est
  transitoire mais l'événement `DocumentReceivedV1` **dead-lette après 5 retries** (V003). Pas de
  mécanisme de rejeu (décision existante « pas de rejeu automatique inventé »). → Le rejeu d'un
  événement déjà dead-letté est un **geste opérateur documenté** (part 3).

## Tâches
- [ ] 1. `IngestDocumentBatchHandler.RetryRangingIfNotRangedAsync` : re-stage quand le document est
      rangé MAIS le staging est absent (`ExistsAsync == false`) — plus de zombie. Vrai doublon
      terminal (rangé + staging présent) → inchangé (aucun effet). Hash garanti par construction
      (chemin atteint car empreinte déjà connue ; clé de staging porte la même empreinte,
      re-vérifiée à la relecture). Log info dédié.
- [ ] 2. `AppBootstrap` : défaut stable du RootPath de staging hors arbre de build
      (`ContentRootPath/App_Data/staging-store`), configurable, prod = volume dédié. Insertion via
      `services.Configure` (s'exécute AVANT le `PostConfigure` BaseDirectory du module).
- [ ] 3. `ADR-0014` : amendement FIX07b — emplacement stable + runbook rejeu dead-letter « contenu
      stagé absent » après re-stage.
- [ ] 4. Tests d'intégration EXÉCUTÉS :
      - Ingestion : re-push d'un document rangé sans staging → re-stage (Count back to 1) ; doublon
        avec staging présent → pas de re-stage (`Duplicate_Document_Is_Not_Re_Staged` reste vert).
      - Pipeline : staging absent → CHECK transitoire (throw, document reste `Detected`, pas de
        blocage inventé) → après re-stage → `ReadyToSend`.
- [ ] 5. verify-fast + run-tests + codex-review propres.

## Frontières respectées
- Aucune modification du socle Stratum (`src/Common`, outbox) — provenance non impactée.
- Re-stage idempotent, content-addressed (aucune résurrection altérée). Le staging reste
  purgeable/transitoire (≠ WORM/audit, CLAUDE.md n°4 inchangé).
- Le re-push agent ne concerne que les documents pas encore « Processed » (staged+Detected) du
  point de vue de l'agent ; resurrection d'un Issued non atteignable en pratique.

## Review
_(à compléter)_
