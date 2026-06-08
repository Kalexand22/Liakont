# WEB08 — Page Réconciliation des PDF (console-web)

Item: WEB08 | Segment: console-web | Sous-branche: feat/console-web-WEB08
Blueprint: blazor-page-item | Slot: 2 | Session: orch-20260608-183515-Liakont3-s2

## Objectif
Page `/reconciliation` : 3 listes (propositions à confirmer, PDF orphelins, documents sans PDF)
alimentées par le module Reconciliation (TRK07/API04), aperçu PDF natif (iframe sur l'endpoint
HTTP existant), compteur dans la navigation, page masquée si la capacité pool n'est pas déclarée.

## Décisions d'architecture (sourcées)
- **Accès données = in-process** (pattern DocumentControlActionsService / DocumentConsoleQueryService) :
  la console appelle les contrats du module (`IReconciliationQueries`/`IReconciliationService`),
  PAS l'endpoint HTTP (cookie OIDC indisponible côté circuit — précédent WEB05/DocumentControls).
- **Aperçu PDF = iframe navigateur** vers `/api/v1/reconciliation/{id}/pdf` (endpoint API04 existant,
  conçu pour l'affichage inline — le navigateur porte le cookie d'auth, pas le circuit serveur).
- **Gating page = capacité pool** via `ILiakontConsoleContext.ReconciliationAvailable` (présence du
  pool ; la capacité `ProvidesUnlinkedDocumentPool` via /settings relève d'API01d GELÉ — heuristique
  d'affichage déjà retenue par WEB01, on la réutilise, aucune règle inventée).
- **Compteur nav** : `NavItem` vendored (Stratum.Common.UI) n'a pas de champ badge ; on NE modifie PAS
  le socle. Le compteur est embarqué dans le libellé (`Réconciliation (N)`). N = propositions + orphelins
  (= éléments en attente d'action opérateur ; documents-sans-PDF est informationnel, exclu du compteur).
- **Lien manuel** : le sélecteur de document = la 3e liste (documents émis SANS PDF) filtrable par n°,
  candidats naturels d'un rattachement (aucune requête nouvelle).
- **Worklist, pas une grille** : 3 files courtes orientées action + aperçu inline → SectionCards + tables
  simples (précédent changelog TableTvaView), pas DeclaredListPage (qui vise grilles filtrables/exportables).
- **Permission** : la file de réconciliation est `liakont.actions` (cf. endpoint API04 + intégration :
  reader → 403). La page vérifie `liakont.actions` (comme TableTva vérifie settings) ; les actions du
  service refont la garde (défense en profondeur).
- **E2E** : sous OIDC, IDN01 (pont rôle→permission) n'est pas mergé → on prouve le gating de capacité
  (page masquée sans pool), pas le clic opérateur permission-gated (précédent DocumentControls/WEB03b/WEB05).
  Le parcours d'action est couvert par bUnit (callbacks de la vue) + intégration (endpoints API04).

## Fichiers
### Host — nouveaux
- [ ] Reconciliation/ReconciliationQueueViewModel.cs (3 listes + CanAct)
- [ ] Reconciliation/ReconciliationActionResult.cs (Ok/Failure)
- [ ] Reconciliation/IReconciliationConsoleService.cs
- [ ] Reconciliation/ReconciliationConsoleService.cs (queries+service+actor+permission)
- [ ] Components/ReconciliationView.razor (vue pure, testable bUnit)
- [ ] Components/Pages/Reconciliation.razor (@page "/reconciliation")

### Host — modifiés
- [ ] Navigation/ILiakontConsoleContext.cs (+ ReconciliationPendingCount)
- [ ] Navigation/LiakontConsoleContext.cs (calcul du compteur via IReconciliationQueries)
- [ ] Navigation/LiakontNavSectionProvider.cs (libellé avec compteur)
- [ ] Startup/AppBootstrap.cs (DI IReconciliationConsoleService)

### Tests
- [ ] Components/ReconciliationViewTests.cs (bUnit : 3 listes, vides, callbacks, iframe, picker)
- [ ] Reconciliation/ReconciliationConsoleServiceTests.cs (permission, identité opérateur, délégation)
- [ ] Navigation/LiakontConsoleContextTests.cs (compteur)
- [ ] Navigation/LiakontNavSectionProviderTests.cs (libellé compteur + fake étendu)
- [ ] tests/Liakont.Tests.E2E/Scenarios/ReconciliationE2ETests.cs (gating : masquée sans pool)

## Vérification
- [x] verify-fast (plateforme .NET 10 + agent net48) — PASS
- [x] run-tests (unit + intégration, bUnit inclus) — PASS (4385 tests)
- [x] run-e2e (Playwright) — PASS (11 tests)
- [ ] codex-review propre (ou P2 acceptés justifiés)

## Review
Round 1 (codex-review -Base feat/console-web) : 0 P1, 4 P2 — tous corrigés.
- P2-1 : compteur nav divulgué aux non-opérateurs → garde `liakont.actions` à l'AFFICHAGE dans
  LiakontNavSectionProvider (au rendu, claims chargés ; pas au calcul du contexte — race à l'ouverture
  du circuit, cf. ClaimsPermissionService.InitializeAsync fire-and-forget).
- P2-2 : GetQueueAsync sans garde alors que le doc la promet → garde `liakont.actions` (retourne
  ReconciliationQueueViewModel.Empty sans interroger le module). Défense en profondeur.
- P2-3 : calcul du compteur au chemin critique d'ouverture du circuit sans isolation → try/catch
  dégradant à 0 (badge décoratif, ne doit pas rendre la console indisponible) + log.
- P2-4 : trou de test page-niveau → ReconciliationTests.cs (bUnit) : unavailable, chargement opérateur,
  restriction sans permission, rechargement après action, échec de chargement.
Round 2 : re-verify-fast PASS, run-tests PASS (4393), run-e2e PASS (11). Review round 2 : voir ci-dessous.
