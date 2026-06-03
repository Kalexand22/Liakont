# Definition of Done — Phase 1

Un item d'orchestration est **done** quand TOUTES ces conditions sont remplies :

## Pour tout item

- [ ] Tous les critères d'acceptation du lot file (`orchestration/items/<lot>.yaml`) sont satisfaits
- [ ] `tools/verify-fast.ps1` passe (build double : plateforme .NET 10 + agent net48 x86, analyzers, tests unitaires)
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

## Pour les gates (executor: human)

- [ ] Tous les items du segment sont done
- [ ] PR créée vers main avec le récapitulatif du segment
- [ ] Validation fonctionnelle humaine effectuée
- [ ] Merge dans main par un humain (jamais par l'IA)
