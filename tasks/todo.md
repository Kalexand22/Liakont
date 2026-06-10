# Housekeeping — suites de l'audit projet (2026-06-10)

Session interactive (hors orchestration), branche `feat/console-web`. Audit complet du repo
(5 agents : architecture, tests, dette, tooling, docs) → verdict globalement sain ; ce lot
traite les actions rapides retenues par Karl.

## Tâches
- [x] Versionner le script de seed démo : `.dev-seed-demo-docs.ps1` (racine, untracked) →
      `tools/dev-seed-demo-docs.ps1` (en-tête révisé : usage, données 100% fictives, clé d'agent jamais versionnée)
- [x] Annoter `deploy/docker/docker-compose.keycloak.yml` : avertissement explicite
      « defaults dev uniquement, jamais en prod sans KC_DB_PASSWORD/KC_ADMIN_PASSWORD »
- [x] bUnit pages Host restantes sans test : `LoginTests` (5 facts : formulaire local,
      redirection OIDC, déjà-authentifié, anti open-redirect, sans HttpContext),
      `LogoutTests` (3 facts : GET sans signout, POST→oidc-logout, POST legacy signout cookie),
      `UserPreferencesPageTests` (2 facts : rendu anonyme sans lecture base, reflet des préférences persistées)
- [x] CLAUDE.md : règle review 19 + checklist — périmètre du P1 « page sans test » précisé :
      pages Liakont (Host + Liakont.Modules.*) ; socle vendored Stratum.Modules.* non modifié exempté
- [x] verify-fast PASS (les 2 solutions ; 10 nouveaux tests bUnit EXÉCUTÉS et verts)
- [x] codex-review boucle propre (round 1 : 1 P2 ; round 2 : clean)

## Constats d'audit NON traités ici (assumés / suivis ailleurs)
- Handlers des modules socle (Identity/Audit/Job) sans tests unitaires : même logique de
  périmètre que les pages socle — couvert par l'exemption consignée dans CLAUDE.md.
- TODO(PIP03b) et TODO(ADR-0012) : items du manifest, hors scope.
- Module Party Contracts-only, double lignée Stratum.*/Liakont.* : design assumé (vendoring tracé).

## Review
- Round 1 : 1 P2 — l'en-tête du script de seed annonçait « montants en decimal » mais les
  littéraux PowerShell sont des double et `totalGross` était une addition IEEE-754.
  Fix : casts `[decimal]` sur totalNet/totalTax/totalGross (sérialisation vérifiée :
  `125 + 6.88 → 131.88` propre via ConvertTo-Json).
- Round 2 : clean (re-review confirmant tests non faux-verts, payload conforme au contrat
  agent v1, narrowing CLAUDE.md assumé). verify-fast PASS antérieur au fix ; le fix ne touche
  qu'un script PowerShell hors périmètre de build — syntaxe revalidée par PSParser.
