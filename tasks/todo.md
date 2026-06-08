# WEB04b — Page Paramétrage du tenant (lecture)

Segment console-web / sous-branche `feat/console-web-WEB04b`. Page lecture seule alimentée par
GET /settings (API01c) + agents (IAgentQueries.ListByTenantAsync, précédent WEB01) + POST
/archive/verify (API03). Bâtie sur le précédent WEB01 (Dashboard) : vue pure testée en bUnit +
page mince + service d'assemblage console dans le Host. AUCUNE logique métier dans la page.

## Plan

- [ ] `Parametrage/ParametrageAgentLine.cs` — ligne agent (Nom, dernier heartbeat, version, état)
- [ ] `Parametrage/ParametrageViewModel.cs` — modèle d'assemblage (Profile, Fiscal, Tva, PaAccounts, Agents)
- [ ] `Parametrage/IParametrageQueries.cs` — interface (GetParametrageAsync + VerifyArchiveIntegrityAsync)
- [ ] `Parametrage/ParametrageQueryService.cs` — agrège /settings + agents ; délègue la vérif d'intégrité
- [ ] `Components/ParametrageView.razor` — rendu pur : profil, fiscal (alerte « décision en attente » si null),
      agents, comptes PA + capacités, résumé table TVA + lien WEB07, secrets masqués, bloc intégrité archive
- [ ] `Components/Pages/Parametrage.razor` — `@page "/parametrage"`, charge le modèle, état de la vérif d'intégrité
- [ ] `AppBootstrap.cs` — enregistrer `IParametrageQueries` → `ParametrageQueryService` (scoped)
- [ ] bUnit `Components/ParametrageViewTests.cs` — profil, alertes fiscales null, secrets masqués, capacités PA,
      résumé TVA + lien, agents, vérif d'intégrité (rapport / erreur / en cours / callback)
- [ ] bUnit `Pages/ParametrageTests.cs` — chargement OK / erreur (bandeau)
- [ ] E2E `Scenarios/ParametrageE2ETests.cs` — login → /parametrage → page affichée
- [ ] verify-fast vert / run-tests vert / run-e2e vert / codex-review clean

## Revue (résultats)
- verify-fast : PASS (build + analyzers + arch tests + bUnit). Bug Razor corrigé : `IntegrityError`
  (string?) doit être lié par `@_integrityError` (sinon littéral → CS0414). SA1515 corrigé (commentaire
  précédé d'une ligne vide dans le test project).
- run-tests : PASS (4166 tests, 0 échec — aucune régression).
- run-e2e : PASS (3 tests E2E, dont `ParametrageE2ETests`).
- Page présentation-only ; assemblage isolé dans `ParametrageQueryService` (précédent WEB01/Dashboard).

## Notes de décision
- Agents : absents de `TenantSettingsOverviewDto` → lus via `IAgentQueries.ListByTenantAsync`
  (même chemin que le Dashboard WEB01), tenant-scopé. Aucune invention.
- Paramètres fiscaux opaques (operationCategory, reportingFrequency) + `vatOnDebits` (bool?) :
  affichés verbatim, alerte « décision en attente » si null — jamais de valeur devinée (CLAUDE.md n°2).
- Lien « table TVA » → route WEB07 en forward-reference (`/parametrage/table-tva`, livrée par WEB07a).
- Critère « parité liste (recherche/filtres/export/multi-sélection) » : non applicable à une vue de
  synthèse (pas de grille d'entités) — précédent WEB01/Dashboard ; aucune grille maison.
