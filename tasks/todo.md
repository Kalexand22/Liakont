# WEB03b — Détail document : actions garde-fou + re-vérification

Segment console-web (feat/console-web), sous-branche feat/console-web-WEB03b, slot-2.
Blueprint blazor-page-item. Dépend de WEB03a (page détail) + API02b (endpoints verdict/recheck).

## Contexte (exploré)
- Page détail = `DocumentDetail.razor` (mince) + `DocumentDetailView.razor` (vue PURE, 4 onglets).
- Endpoints API02b sur `DocumentActionsEndpointMapping` : POST /verdict (confirm_b2c / handle_manually),
  POST /recheck. Service direct (IDocumentLifecycle / IDocumentRecheckService), pas de MediatR.
- Auth OIDC : aucun claim `permission` posé pour les rôles non super-admin (pont IDN01 non implémenté,
  spec seulement). Précédent WEB07a : E2E prouve navigation→rendu ; le détail gated reste couvert par bUnit.
- Pas de code de règle structuré persisté sur le blocage (texte libre). Signal structuré pour B2B/B2C :
  `CustomerIsCompanyHint && !BuyerConfirmedAsIndividual` (indice « société » source, conservateur).

## Décisions
- Service Host in-process `IDocumentControlActions` (mirror EXACT des endpoints verdict/recheck :
  mêmes codes d'audit, même identité opérateur, mêmes gardes d'état, result records sans exception).
  In-process obligatoire (cookie OIDC indisponible dans le circuit — précédent WEB05).
- Vue PURE étendue : boutons dans l'onglet Contrôles, gated par param `CanAct` (la page calcule
  `HasPermission(liakont.actions)`). Verdict visible si Blocked + indice société + non confirmé ;
  recheck visible pour tout Blocked. Boutons masqués si !CanAct (lecture seule conforme WEB03a).
- Après action : la page recharge le modèle (re-query) → Historique + Contrôles à jour immédiatement.

## Tâches
- [ ] `IDocumentControlActions.cs` + `DocumentControlActionsService.cs` (+ enum ConsoleVerdict, result record)
- [ ] `DocumentDetailView.razor` : boutons Contrôles + params (CanAct, callbacks, feedback) + css scoped
- [ ] `DocumentDetail.razor` : inject IPermissionService + IDocumentControlActions, handlers + reload
- [ ] `AppBootstrap.cs` : enregistrer IDocumentControlActions (Scoped)
- [ ] bUnit `DocumentControlActionsServiceTests` : verdict confirm/manual, recheck (tous outcomes), gardes
- [ ] bUnit `DocumentDetailViewTests` (+) : boutons visibles/masqués, callbacks, feedback
- [ ] bUnit `DocumentDetailTests` (+) : permission end-to-end + reload après action
- [ ] E2E `DocumentControlsE2ETests` : login → doc bloqué → onglet Contrôles rendu, boutons absents (lecture)
- [ ] verify-fast + run-tests + run-e2e + codex-review loop + merge-back

## Review
- verify-fast PASS (plateforme .NET10 + agent net48) ; run-tests PASS (4336) ; run-e2e PASS (9, dont DocumentControlsE2ETests).
- codex-review round 1 : 0 P1 / 3 P2 — toutes corrigées :
  - P2#1 garde liakont.actions côté service (défense en profondeur) + test du refus.
  - P2#2 DocumentActionContract = source unique des identifiants d'action (états, verdict, codes d'audit, motif B2B), consommée par l'endpoint API02b ET le service console (fidélité piste d'audit).
  - P2#3 test page mapping bouton→verdict (anti-inversion B2C↔B2B).
- codex-review round 2 : CLEAN (No findings).
- Décision clé : signal d'affichage du verdict = `Blocked && CustomerIsCompanyHint && !BuyerConfirmedAsIndividual` (structuré, conservateur ; cas TVA/forme-juridique-seule résoluble via WEB03c). E2E « opérateur clique sous OIDC » porté par WEB05/IDN01 (non câblé à WEB03b par l'opérateur).
