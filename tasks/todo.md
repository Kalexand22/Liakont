# SUP02 — Dashboard de supervision (opérateur d'instance)

Branche : `feat/console-web-SUP02` (segment console-web). Blueprint : blazor-page-item.

## Décisions d'architecture (vérifiées sur pièce)
- Lecture cross-tenant = SEULEMENT dans le module Supervision (CLAUDE.md #9/#14). L'agrégateur
  `CrossTenantSupervisionDashboardQueries` vit dans Supervision.Infrastructure ; interface +
  DTOs slim dans Supervision.Contracts (Contracts garde sa seule dép : Common.Abstractions).
- Énumération tenants : `ITenantQueries.ListAsync()` (base système, indép. du tenant ambiant).
- Scope par tenant depuis HTTP : `ITenantScopeFactory.Create(tenantId)` → `ITenantScope`
  (précédent : FanOutPortalQueryService ; fan-out parallèle, skip+log sur tenant en échec).
- Gate serveur de la page : `[Authorize(Policy = LiakontPermissions.Supervision)]`
  (PermissionPolicyProvider crée la policy ; bloque une nav directe d'un non-superviseur =
  empêche une fuite cross-tenant). + nav déjà gated (LiakontNavSectionProvider).
- Données NON fabriquées (producteurs gelés SUP01c / arbitrage fiscal D-a) : taille de file de
  push, historique des heartbeats, échéance déclarative J-3 → NON affichées (no false-green).
  Affiché : alertes (IAlertQueries), compteurs documents par état (IDocumentQueries.CountsByState),
  état agent last-heartbeat + version (IAgentQueries).

## Tâches
- [x] DTOs Contracts : AgentStatusDto, TenantSupervisionRowDto, TenantSupervisionDetailDto, AlertDto
- [x] Interface Contracts : ISupervisionDashboardQueries (overview / detail / acknowledge)
- [x] Impl Infrastructure : CrossTenantSupervisionDashboardQueries (fan-out cross-tenant)
- [x] csproj Host : + Supervision.Contracts ; registration DI (AddSupervisionModule)
- [x] Page Host /supervision (overview, DeclaredListPage<TenantSupervisionRowDto>)
- [x] Page Host /supervision/{tenantId} (détail : agents + alertes actives + Acquitter)
- [x] Column registries + templates (Host : SupervisionTenant/AlertColumnRegistry, SupervisionDisplay)
- [x] bUnit : SupervisionTests + SupervisionDetailTests (rendu + acquittement)
- [x] Unit : CrossTenantSupervisionDashboardQueriesTests (fan-out, tri, résilience, acquittement scopé)
- [x] E2E : SupervisionDashboardE2ETests (superviseur atteint /supervision, dashboard rend sans erreur) ;
      frontière de permission déjà couverte par PermissionGatedNavE2ETests + DashboardE2ETests
- [x] verify-fast vert / run-tests vert / codex-review (boucle) ; run-e2e à exécuter sur l'arbre intégré

## Review
Item REPRIS le 2026-06-09 (session orch-20260609-061319-Liakont2-s1) : la session slot-1 d'origine
(2026-06-08) est morte tôt (lease expiré ~9 h, aucun heartbeat renouvelé) en laissant le travail SUP02
non commité dans l'arbre. Reprise non-destructive (claimed→in_progress), pipeline mené à terme.

- verify-fast : PASS (après fix_verify délégué — SA1204 réordonnancement statique/instance dans
  CrossTenantSupervisionDashboardQueries ; CA1859 type concret ; SA1402/SA1649 un type par fichier
  → split de ConsolePageTestStubs en StubSharedResourcesLocalizer/TestActorContextAccessor/
  NullGridPreferences/NullSavedFilters).
- run-tests : PASS (3 suites, 4428 tests, 0 échec).
- E2E : ajout de SupervisionDashboardE2ETests (parcours superviseur → /supervision → rendu). À exécuter
  via run-e2e sur l'arbre synchronisé avec feat/console-web (WEB08 + main).
- Décisions de périmètre : pas de taille de file de push, pas d'historique heartbeats, pas d'échéance J-3
  (producteurs gelés SUP01c / arbitrage fiscal D-a) — aucune donnée fabriquée. Acquittement E2E (seed
  d'alerte) laissé au bUnit (routage tenant/alerte/opérateur prouvé) ; la frontière de permission est
  E2E-prouvée (rôle realm → claim → nav gating). Lecture cross-tenant confinée au module Supervision.
