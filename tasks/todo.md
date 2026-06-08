# WEB01 — Navigation Liakont + tableau de bord d'accueil

Branche : `feat/console-web-WEB01` (segment `feat/console-web`). Blueprint `blazor-page-item`. Slot 3.

## Contexte / décisions d'architecture

- **Host = foyer du dashboard et de la nav maître** : le tableau de bord est cross-module
  (Documents + Ingestion/agents + TenantSettings/TVA) et la nav maître Liakont est transverse.
  Les modules `*.Web` sont API-only (`Microsoft.NET.Sdk`) ; le Host (`Liakont.Host`) est l'app
  Blazor Server + racine de composition qui référence tous les modules. → pages + nav dans le Host.
- **Sources de données (toutes existantes, en Contracts)** :
  - compteurs par état → `IDocumentQueries.GetDocumentsAsync` → `DocumentListResult.CountsByState`
  - état agent (heartbeat/version) → `IAgentQueries.ListByTenantAsync(tenantId)` → `AgentSummaryDto`
  - état TVA + fiscal (reportingFrequency) → `ITenantSettingsConsoleQueries.GetSettingsOverview()`
- **Échéance déclarative** : `reportingFrequency` est une chaîne OPAQUE (F12-A §3.3). On NE CALCULE
  aucune date (pas de règle de cadence sourcée → invention interdite, R2). null/vide → bandeau
  « Fréquence déclarative non renseignée » ; renseigné → on affiche la cadence déclarée telle quelle.
- **Nav conditionnelle** :
  - Supervision → gatée par `IPermissionService.HasPermission(liakont.supervision)` (synchrone).
  - Réconciliation → la spec cite « capacité pool PDF via GET /settings », MAIS
    `TenantSettingsOverviewDto` n'expose AUCUNE capacité agent (réservé à API01d, GELÉ — confirmé
    en doc du DTO). Signal disponible et honnête = PRÉSENCE du pool PDF du tenant
    (`IIngestedPdfStore.ListPooledPdfsAsync(tenant).Any()`) : un agent qui « fournit le pool » est
    exactement celui qui y dépose des PDF. Aucune règle fiscale, simple heuristique d'affichage.
    Déviation documentée (session log).
  - Mécanisme : `BuildNavTree` omet les sections à 0 item ; un `INavSectionProvider` SCOPED retourne
    l'item conditionnel seulement si la condition est vraie. Pré-chargement déterministe du flag pool
    via un `CircuitHandler` (avant rendu de la nav), miroir du pattern `TenantCircuitHandler`.
- **DocumentStateBadge** : composant présentational bâti sur `StatusBadge` (Intent=Severity), prend
  l'état en `string` (clé de `CountsByState`, découplé du Domain), total sur l'enum + fallback.

## Tâches

- [ ] `DocumentStateBadge.razor` (Host/Components) : 8 états F10 §2.2 (+ ReadyToSend, fallback),
      emoji + libellé FR + Severity. Réutilise `StatusBadge`.
- [ ] `DashboardViewModel.cs` (Host/Dashboard) : compteurs, agents, état TVA, reportingFrequency.
- [ ] `DashboardView.razor` (présentational, paramètres) : compteurs (badges), état agent,
      alerte TVA NON VALIDÉE, bandeau/cadence déclarative. `SectionCard`, 100 % français.
- [ ] `Dashboard` (page `/`, remplace Home.razor) : charge les 3 queries (tenant-scopé), passe au View.
- [ ] `ILiakontConsoleContext` + `LiakontConsoleContext` + `LiakontConsoleCircuitHandler`
      (Host/Navigation) : flag `ReconciliationAvailable` chargé au circuit.
- [ ] `LiakontNavSectionProvider.cs` (Host/Navigation, SCOPED) : section « Liakont » avec items
      Documents/Encaissements/Traitements/[Réconciliation]/Paramétrage/[Supervision].
- [ ] DI dans `AppBootstrap.cs` : nav provider scoped + console context + circuit handler.
- [ ] Tests bUnit (`tests/Host/.../`) : DocumentStateBadge (tous états + FR), DashboardView
      (compteurs, alerte TVA, bandeau null vs cadence), LiakontNavSectionProvider (Réconciliation
      masquée sans pool, Supervision masquée sans permission). Ajout package `bunit`.
- [ ] Test E2E Playwright (`tests/Liakont.Tests.E2E/Scenarios/`) : login → dashboard rendu →
      section nav Liakont présente. `[Trait("Category","E2E")]`. MAJ assertion LoginShell (heading `/`).
- [ ] verify-fast + run-tests + run-e2e verts ; codex-review propre.

## Review

Tous les fichiers prévus créés/modifiés. Découpage final :
- Présentation : `DocumentStateBadge.razor` + `DocumentStateDisplay.cs` (emoji en échappement `\U…`,
  insensible à l'encodage), `DashboardView.razor` (+ `.razor.css`), page `Home.razor` (mince).
- Modèle : `DashboardViewModel` + sous-records (1 type/fichier, SA1402).
- Composition : `IDashboardQueries` + `DashboardQueryService` (assemblage hors page, testable).
- Navigation : `LiakontNavSectionProvider` (scoped) + `ILiakontConsoleContext` /
  `LiakontConsoleContext` / `LiakontConsoleCircuitHandler` (pré-chargement déterministe au circuit).
- DI : `AppBootstrap.cs`.

Décisions notables (rien d'inventé) :
- `reportingFrequency` opaque → cadence affichée telle quelle, AUCUNE échéance calculée (R2) ; null → bandeau.
- Réconciliation gatée sur la PRÉSENCE réelle du pool PDF (`IIngestedPdfStore`), car le read-model
  de capacités agent (API01d) est gelé et non exposé par GET /settings. Heuristique d'affichage, pas une règle fiscale.
- Supervision gatée par `IPermissionService` (synchrone, claims).
- Dashboard + nav maître logés dans le Host (cross-module, racine de composition) — modules `*.Web` API-only.

Vérification :
- [x] verify-fast (plateforme .NET 10 + agent net48) — PASS
- [x] run-tests (suite complète) — PASS (4064 tests, 0 échec ; round 2 ajoute des tests unitaires)
- [x] run-e2e (Playwright) — PASS (2 tests E2E : LoginShell + Dashboard) [round 1 ; round-2 sans impact E2E]
- [x] codex-review -Base feat/console-web : round 1 = 0 P1 / 3 P2 (tous des trous de test) → corrigés ;
      round 2 attendu clean.

Réponse aux 3 P2 (round 1) — tous CORRIGÉS :
1. Faux-vert chemin données dashboard → ajout `HomeTests` (bUnit) : succès = `liakont-dashboard`,
   échec `IDashboardQueries` = bandeau `dashboard-error` sans `liakont-dashboard`.
2. Trou de test `LiakontConsoleContext` réel → ajout `LiakontConsoleContextTests` : pool plein → vrai,
   vide/tenant absent → faux (store non appelé), idempotence (`EnsureInitializedAsync` lit le pool une fois).
3. Encodage emoji incohérent → les 9 emoji des libellés passés en échappement `\U…` (10 escapes : 9 + FE0F),
   commentaire de classe corrigé, commentaires en notation `U+`.
