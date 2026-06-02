# Definition of Done — Phase 1

Un item d'orchestration est **done** quand TOUTES ces conditions sont remplies :

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
