# Definition of Done — Phase 1

Un item d'orchestration est **done** quand TOUTES ces conditions sont remplies :

## Commandes de vérification (réelles)

Les vérifications ci-dessous s'exécutent depuis la racine du dépôt. Toutes échouent avec un
code de sortie non nul en cas de problème (pas de faux vert).

| Vérification | Commande | Portée |
|---|---|---|
| Rapide (locale) | `powershell -ExecutionPolicy Bypass -File tools/verify-fast.ps1` | structure + manifest + restore + build x86 + tests unitaires |
| Suite complète | `powershell -ExecutionPolicy Bypass -File tools/run-tests.ps1` | unit + intégration + contrat PA (exclut `Category=Staging`, `Sandbox`, `Integration.SqlServer`) |
| Review | `powershell -ExecutionPolicy Bypass -File tools/codex-review.ps1` | review de l'arbre de travail courant |
| CI (push/PR) | `.github/workflows/ci.yml` | build **x86 ET x64** (matrice) + tests, sur `windows-latest` |

Détails sous-jacents :

- `verify-fast.ps1` construit la plateforme **x86** uniquement (plateforme contraignante des
  drivers ODBC Pervasive 32 bits) pour rester rapide ; la couverture **x64** est assurée par
  la CI. Logs détaillés dans `.verify-fast.log`.
- `run-tests.ps1` exécute `dotnet test src/Gateway.sln` avec le filtre
  `Category!=Staging&Category!=Sandbox&Category!=Integration.SqlServer`. Résumé compact sur
  stdout, log détaillé dans `.run-tests.log`.
- La CI construit les **deux** plateformes (matrice `x86`/`x64`) en `Release` ; une étape en
  échec fait échouer le pipeline (aucun `continue-on-error`).
- Les filtres de catégories diffèrent volontairement : `verify-fast.ps1` exécute un sous-ensemble « rapide » (`Category!=Integration&Category!=Staging`) tandis que `run-tests.ps1` et la CI excluent `Staging`, `Sandbox` et `Integration.SqlServer`. Aucun test ne porte aujourd'hui ces catégories (l'écart est latent). La taxonomie de référence des catégories de tests est définie dans `docs/architecture/testing-strategy.md` (item SOL03) ; l'harmonisation des filtres y est rattachée.

## Pour tout item

- [ ] Tous les critères d'acceptation du lot file (`orchestration/items/<lot>.yaml`) sont satisfaits
- [ ] `tools/verify-fast.ps1` passe (build + analyzers + tests unitaires)
- [ ] `tools/codex-review.ps1` : clean ou uniquement des P2 acceptés avec justification écrite
- [ ] Le code est commité en conventional commits et mergé `--no-ff` dans la branche de segment
- [ ] Le log de session est écrit dans `$ORCH_REPO/session-log/`

## En plus, pour les items de code (module-work-item)

- [ ] Les tests sont EXÉCUTÉS et verts (pas seulement écrits)
- [ ] Aucun float/double sur un montant
- [ ] Aucune règle fiscale sans source traçable dans docs/conception/
- [ ] La frontière Core/Adaptateur est respectée
- [ ] Aucun secret en clair

## En plus, pour les items WPF (wpf-screen-item)

- [ ] Tests unitaires du ViewModel verts
- [ ] Checklist smoke mise à jour dans docs/architecture/smoke-checklists/
- [ ] Tous les libellés opérateur en français

## En plus, pour les items de tooling (tooling-item)

- [ ] Le tool a été testé sur état vide, état sale, état d'échec (pas seulement le chemin nominal)
- [ ] Un échec du tool produit un exit code non-zéro (pas de faux vert)

## Pour les gates (executor: human)

- [ ] Tous les items du segment sont done
- [ ] PR créée vers main avec le récapitulatif du segment
- [ ] Validation fonctionnelle humaine effectuée
- [ ] Merge dans main par un humain (jamais par l'IA)
