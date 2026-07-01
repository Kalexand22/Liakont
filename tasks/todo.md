# Re-livraison des trous UX de la recette Bucodi (lot RBF) sur main courant

## Contexte
Audit des branches `feat/recette-bucodi` (lot RBF) + `feat/recette-bucodi-RBF02` vs `origin/main` :
le cœur fiscal/correctness du lot RBF est **déjà dans main** (souvent en mieux). Seuls 3 trous
UX/confort (non fiscaux) restaient absents. Décision Karl : **re-livrer frais depuis main**
(la branche est 169 commits derrière → pas de cherry-pick/merge), puis supprimer les 2 branches.

Branche de travail : `feat/recette-bucodi-ux-gaps` (depuis origin/main `2825872e`).

## Items à porter (adaptés à la forme actuelle de main)

- [ ] **RBF08** — préférences/densité appliquées dès le login
  - `UserPreferencesHydrator.razor` (neuf) monté dans `ErpShellLayout.razor`
  - `UserPreferencesSupport.cs` (neuf) + retouches `App.razor`, `UserPreferencesPanel.razor`
  - Tests bUnit + support + E2E (P1 : page Blazor Liakont sans test = bloquant)
- [ ] **RBF07-A** — refresh liste + compteurs après envoi, sans rechargement complet
  - `Documents.razor` (rappel `ReloadPeriodAsync()` après envoi)
- [ ] **RBF07-B** — différés traités en INFO (fin du faux-rouge « aucun document émis »)
  - `SendTally.Deferred` exposé + wording
  - `PipelineRunLogDto.DocumentsDeferred` (absent de main) + persistance/lecture
  - Migration `documents_deferred` : **renumérotée** (V006 collisionne avec
    `V006__create_b2c_margin_emissions_table.sql`) → prochain n° libre
  - `DocumentSendActionsService.DescribeSendRunOutcome` : branche INFO si `failed==0 && pending>0`
  - Tests (DocumentSendActionsServiceTests, DocumentsTests)

## Garde-fous
- Ne JAMAIS affaiblir une validation Blocking (RBF07-B ne touche qu'au RAPPORT d'envoi).
- Surgical : seulement ce que chaque item exige.

## Vérification (obligatoire avant « fini »)
- [ ] verify-fast (plateforme .NET 10 + agent net48) vert
- [ ] run-tests (intégration) si endpoints/DI/migration touchés
- [ ] codex-review boucle propre (P1/P2 corrigés)
- [ ] Build Release (StyleCop) avant de déclarer vert

## Clôture
- [ ] Commit + push de `feat/recette-bucodi-ux-gaps`
- [ ] Suppression `origin/feat/recette-bucodi-RBF02` (subsumée) et `origin/feat/recette-bucodi`

## Notes de suivi (hors périmètre de cette branche)
- 🧹 22 réfs `CLAUDE.md n°X` fuitent dans le source du plug-in SuperPDP (src/PaClients/...SuperPdp) → scrub séparé.
- 🏁 `GATE_RECETTE_BUCODI` reste `pending` côté orchestration → gate orpheline une fois ce lot absorbé (nettoyage runner, pas édition manuelle).

## Review (bilan)

Livré sur `feat/recette-bucodi-ux-gaps` (depuis origin/main `2825872e`) :
- **RBF08** porté à l'identique (hydrateur de shell + support + retouches App/Panel) avec bUnit + E2E.
- **RBF07-A** (refresh sans rechargement) et **RBF07-B** (différés en INFO) portés ; migration **V009**
  (renommée `add_send_outcome_counters`, n° libre après V006-V008).

Correctifs review (2 rounds, boucle close au round 3 = CLEAN) :
- **[P2 round 1]** `Deferred` agrégeait du transitoire ET des HOLD opérateur (`EmitterUnresolved`/`TvaUnresolved`)
  → un run tout-HOLD passait en vert « en cours d'émission » (succès silencieux, n°3). **Fix** : nouvel outcome
  `Held` distinct (compteur + colonne `documents_held` + DTO/RunLog/store/queries) ; la branche verte n'est prise
  que sans aucun HOLD ; le HOLD est signalé avec son action corrective (n°12). 4 tests ajoutés.
- **[P2 round 1]** test E2E préférences auto-empoisonnant (teardown best-effort à exception avalée) → **fix** :
  reset idempotent GARANTI en setup.
- **[P2 round 2]** `UserPreferencesSupport` : `JsonNode.Parse(null)` lèverait `ArgumentNullException` non rattrapée
  → **fix** : coalescence `?? "{}"` + test null.
- **[P2 round 2]** branche `emitted>0` ne nommait pas l'action corrective HOLD inline → **fix** additif + test.

Vérification : verify-fast ✅ (plateforme .NET 10 + agent net48), build Release ✅ (StyleCop), run-tests
intégration ✅ (7245 tests, round-trip `documents_deferred`/`documents_held` exercé), codex-review ✅ round 3 CLEAN.

## Clôture (état)
- [x] verify-fast / Release / run-tests / review CLEAN
- [x] Commit + push de `feat/recette-bucodi-ux-gaps` (e6040d7d) — PR à ouvrir / merge humain
- [x] Suppression `origin/feat/recette-bucodi-RBF02` (déjà supprimée par un tiers) + `origin/feat/recette-bucodi`
