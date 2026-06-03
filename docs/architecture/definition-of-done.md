# Definition of Done — Phase 1

Un item d'orchestration est **done** quand TOUTES ces conditions sont remplies :

## Pour tout item

- [ ] Tous les critères d'acceptation du lot file (`orchestration/items/<lot>.yaml`) sont satisfaits
- [ ] `tools/verify-fast.ps1` passe (build double : plateforme .NET 10 + agent net48 x86, analyzers, tests
      unitaires, **garde de provenance du socle vendored**)
- [ ] `tools/codex-review.ps1` : clean ou uniquement des P2 acceptés avec justification écrite
- [ ] Le code est commité en conventional commits et mergé `--no-ff` dans la branche de segment
- [ ] Le log de session est écrit dans `$ORCH_REPO/session-log/`

## En plus, pour les items de code (module-work-item)

- [ ] Les tests sont EXÉCUTÉS et verts (pas seulement écrits) — `tools/run-tests.ps1`
- [ ] Aucun float/double sur un montant
- [ ] Aucune règle fiscale sans source traçable dans docs/conception/
- [ ] Les frontières de modules sont respectées : inter-modules via Contracts uniquement (NetArchTest),
      Transmission ne référence jamais un plug-in PA concret, l'agent ne référence jamais la plateforme,
      aucune logique métier dans l'agent
- [ ] Toute requête métier est tenant-scopée (seul le module Supervision lit cross-tenant, en lecture)
- [ ] Tout fichier du socle vendored (`Stratum.*`) modifié est consigné dans
      `docs/architecture/provenance-socle-stratum.md`
- [ ] Tout nouveau module sous `src/Modules/` contient MODULE.md, INVARIANTS.md et SCENARIOS.md
      (exigés par les ModuleIsolationTests du socle)
- [ ] Aucun secret en clair

## En plus, pour les items de pages web (blazor-page-item)

- [ ] Tests bUnit des composants verts (rendu par état, libellés, visibilité par permission)
- [ ] Tests E2E Playwright écrits ET EXÉCUTÉS via `tools/run-e2e.ps1` (suite séparée Category=E2E —
      un test E2E écrit mais jamais exécuté est un faux vert)
- [ ] Aucune logique métier dans les pages/composants : tout passe par les handlers MediatR
      (aucun accès direct à la base depuis une page)
- [ ] Tous les libellés opérateur en français, vocabulaire de F10

## En plus, pour les items de tooling (tooling-item)

- [ ] Le tool a été testé sur état vide, état sale, état d'échec (pas seulement le chemin nominal)
- [ ] Un échec du tool produit un exit code non-zéro (pas de faux vert)

## En plus, pour les items agent (lot AGT — net48)

- [ ] Build et tests x86 ET x64 verts (`tools/run-tests.ps1`)
- [ ] Lecture seule stricte de la base source (aucun INSERT/UPDATE/DELETE/verrou)
- [ ] Aucune logique métier (pas de TVA, pas de validation, pas de machine à états)
- [ ] Secrets chiffrés DPAPI, jamais en clair

## Commandes de vérification (référence — branchées sur les solutions réelles par SOL03)

Toutes les commandes se lancent depuis la racine du dépôt, en PowerShell.

| But | Commande | Couvre |
|---|---|---|
| Gate rapide locale | `powershell -ExecutionPolicy Bypass -File tools/verify-fast.ps1` | build plateforme (`src/Liakont.sln`) + build agent x86 (`agent/Liakont.Agent.sln`) + analyzers + tests unitaires (dont architecture/NetArchTest) + garde de provenance du socle vendored |
| Suite complète | `powershell -ExecutionPolicy Bypass -File tools/run-tests.ps1` | unit + intégration Testcontainers PostgreSQL (plateforme) + agent x86 **et** x64. Exclut `Category=Staging\|Sandbox\|E2E`. Échoue si zéro suite exécutée (anti faux-vert) |
| Suite E2E (séparée) | `powershell -ExecutionPolicy Bypass -File tools/run-e2e.ps1` | Playwright `Category=E2E` (livré par SOL05 — décision D3 2026-06-03). Jamais dans `run-tests`/`verify-fast` |
| Garde de provenance | `powershell -ExecutionPolicy Bypass -File tools/socle-provenance-check.ps1` | échoue (exit 2) si un fichier `Stratum.*` vendored a dérivé du baseline (`tools/socle-baseline.sha1`) sans être consigné dans `provenance-socle-stratum.md`. Régénérer le baseline après consignation : `-Generate` |
| Review | `powershell -ExecutionPolicy Bypass -File tools/codex-review.ps1 -Base <branche-segment>` | review du diff de la sous-branche (P1/P2) |

**CI** (`.github/workflows/ci.yml`, sur push/PR) : 2 jobs reproduisant ces vérifications avec
`dotnet` natif — job **plateforme** (`ubuntu-latest`, .NET 10, intégration Testcontainers via le
Docker du runner) et job **agent** (`windows-latest`, net48 x86 **et** x64). Les steps de test
passent par `tools/ci-test.ps1`, qui reproduit la garde anti faux-vert « zéro test exécuté » de
`run-tests.ps1` (un `dotnet test` nu retourne 0 si aucun test ne matche). Tout step en échec fait
échouer le pipeline ; les E2E sont exclues par filtre (`Category!=E2E`) et tournent via
`run-e2e.ps1`.

## Pour les gates (executor: human)

- [ ] Tous les items du segment sont done
- [ ] PR créée vers main avec le récapitulatif du segment
- [ ] Validation fonctionnelle humaine effectuée
- [ ] Merge dans main par un humain (jamais par l'IA)
