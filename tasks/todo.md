# Lot bugs console — issues GitHub #31/#32/#33 + bug-inbox console-web (2026-06-10)

Session interactive autonome (hors orchestration), branche `feat/console-web`. Sources :
les 3 issues GitHub ouvertes (captures BugCapture) + les 4 entrées P2 de
`bug-inbox.jsonl` (repo d'orchestration, tests humains GATE_CONSOLE_WEB).

## Tâches
- [x] Bug-inbox « collision Agents » : entrée nav Liakont renommée « Agents d'extraction »
      (`LiakontNavSectionProvider`) — libellé socle Stratum intouché ; +2 tests nav
- [x] Issue #31 (en-tête redondant) : h1 des pages de section masqués visuellement
      (`.visually-hidden`, a11y conservée) ; l'onglet TabBar devient le seul titre visible →
      segments Liakont ajoutés à `LocalizedTabTitleProvider` + clés resx FR accentuées.
      Les pages de DÉTAIL (Document n°, Supervision tenant) gardent leur h1 (info propre)
- [x] Issue #32 (blocs/carrés) : `DocumentCountsBanner` re-stylé en pastilles compactes
      (nombre + badge côte à côte, arrondis, transitions, tabular-nums) — CSS uniquement
- [x] Issue #33 (filtres perdus) : filtres période/état/type publiés dans l'URL
      (`du/au/etat/type`, replace, coexiste avec les `filter=` de DeclaredListPage) +
      mémoire de circuit `DocumentsListFilterMemory` pour le lien statique « Retour à la
      liste » ; restauration URL > mémoire > défauts, tolérante ; +4 tests bUnit
- [x] Bug-inbox « langue jamais appliquée » (décision Karl) : `DefaultCulture = fr` ;
      `PersistedLanguageRequestCultureProvider` (base = source de vérité, cache 5 min
      invalidé au changement, cookie = repli anonyme) ; middleware localisation déplacé
      APRÈS l'authentification ; `IsLanguageActive` aligné sur la culture effective ;
      +8 tests provider
- [x] Bug-inbox « company context » (triage) : cause dev = seed sans company_id → claim
      `company_id` codé en dur dans le realm dev (société fictive). La dégradation
      gracieuse des pages Notification reste NON faite : socle vendored (modification =
      provenance), l'exception reste visible et tracée — assumé
- [x] Bug-inbox « amorçage console » : realm dev usernames courts (lecture/operateur/
      parametrage/superviseur, e-mails conservés) ; `DevTenantSeeder` (Development +
      section `DevTenantSeed` uniquement, idempotent, base système seulement) amorce le
      tenant `default` rattaché à `liakont-dev` ; claim `iss` conservé dans le cookie
      (`ClaimActions.Remove("iss")`) → `OidcIssuerTenantResolver` fonctionne sur le
      circuit sans sous-domaine ; doc `deploy/docker/README.md`. Le chemin SystemAdmin /
      `/admin/tenants` reste production (documenté), non requis pour l'amorçage dev
- [x] verify-fast PASS (2 solutions) ; tests unit Host verts (dont 16 nouveaux)
- [x] run-tests (intégration) PASS — 4 572 tests (plateforme + agent x86/x64)
- [x] codex-review boucle propre (4 rounds)

## Review
- Round 1 : 1 P2 — le provider de culture lisait la base SYSTÈME (localisation avant
  résolution du tenant) alors que identity.user_preferences est PER-TENANT : en
  database-per-tenant la préférence n'aurait jamais été relue. Fix : middleware
  `UseRequestLocalization` déplacé APRÈS `UseStratumMultiTenancy`.
- Round 2 : 1 P2 — contrat « changer la langue invalide UserCultureCache » non testé
  (régression silencieuse possible). Fix : fact bUnit `Switching_The_Language_Invalidates_
  The_User_Culture_Cache` (cache pré-rempli, clic « fr », cache vidé + préférence persistée).
- Round 3 : 1 P2 — `RestoreFilters` depuis l'URL n'alimentait pas la mémoire de circuit :
  lien partagé → fiche → « Retour à la liste » perdait les filtres. Fix :
  `FilterMemory.Remember(...)` avant le `return` + assertion dans le test de restauration URL.
- Round 4 : CLEAN. verify-fast + tests re-exécutés après le dernier changement de code.
