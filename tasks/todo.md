# TODO — Session autonome nuit 2026-06-17 (Karl dort, « tout fix à la suite »)

Branche : `feat/emitter-filled-by-platform`. Règle : chaque item = verify-fast + tests + review +
commit+push (commit ⇒ push). Ordre validé par Karl : RB9 → **SuperPDP (option 1, prioritaire)** →
RB7 → RB8 → RB6.

## 1. RB9 — ✅ LIVRÉ (commits 33152bb + e038d6d, poussé)
- [x] P2.1 garde SEND `EmitterUnresolved` (HOLD si émetteur nul) + log opérateur FR (EventId 7218).
- [x] P2.3 hoist profil/fiscal en tête de job SEND (fin du N+1 par document).
- [x] P2.2 tests : lock ingestion + CHECK (remplit/bloque) + SEND (HOLD/remplit) + enrichisseur pur.
- [x] verify-fast PASS + run-tests PASS (6236) + codex-review round 3 CLEAN.

## 2. SuperPDP — option 1 (PRIORITAIRE) — plan détaillé : plan-superpdp-option1.md
- [x] **Slice 1 — socle générique mode d'auth** (LIVRÉ, vert) : `PaAuthMode` (Transmission.Contracts),
      `IPaClientFactory.AuthMode` (membre par défaut = ApiKey), `IPaClientRegistry.DescribeAuthModes()`
      (défaut ApiKey, surcharge réelle dans PaClientRegistry), SuperPdpClientFactory déclare OAuth2.
      Tests : PaClientRegistryTests.DescribeAuthModes + SuperPdpClientFactoryTests.AuthMode.
- [ ] **Slice 2 — stockage chiffré OAuth** : migration V011 (encrypted_client_id/secret), PaAccount
      (+2 champs+setters), Add/UpdatePaAccountCommand (+ClientId/ClientSecret), handlers (Protect),
      UoW (INSERT/UPDATE/SELECT/Reconstitute), PaAccountDto (+HasClientId/HasClientSecret), queries.
- [ ] **Slice 3 — câblage + résolveur** : IPaAccountSecretStore (TenantSettings) + impl Postgres ;
      SuperPdpAccountResolver (Host, lit le store, déchiffre, mappe l'env) ; Host csproj ref SuperPdp +
      AddSuperPdpPaClient() + TryAddSingleton resolver (composition root). → SuperPDP apparaît dans la liste.
- [ ] **Slice 4 — formulaire** : ComptesPaView conditionnel au AuthMode (clé API vs client_id/secret,
      accountId requis pour OAuth) + PaAccountConsoleService/Model + bUnit.
- [ ] verify + run-tests + codex-review → commit+push. (SuperPDP = Sandbox only, BaseUrl lève en Prod, F14 §12 O1.)
- NOTE NUIT : socle (slice 1) sécurisé+poussé ; slices 2-4 (secrets+migration+UI, P1) laissées prêtes à
  finir en session dédiée plutôt que rush-buildées non relues. Suite de nuit : RB7 → RB8 → RB6.

## 3. RB7 — ✅ LIVRÉ (le wizard démarre le service après install)
- [x] `AgentProcessDeployer.TryStartService` : démarre + attend *Running* (30 s) après install/check-config ;
      échec de démarrage = avertissement `[!]` + action corrective, ne défait PAS l'install.
- [x] verify-fast + run-tests (6238) verts ; codex-review propre (1 P2 doc-only adressé).
- COUVERTURE = recette manuelle (déployeur de prod non mockable) → Karl vérifie en recette.

## 4. RB8 — DÉFÉRÉ (cadence figée 1 min ; planification non appliquée)
- Enforcement réel = le runner gate les runs sur `EffectiveExtractionPlan`. BLOQUEURS pour une session de
  nuit : (a) la planif PLATEFORME est une expression **cron** → parseur cron = **NuGet + ADR** (interdit
  sans aval Karl, CLAUDE.md) ; (b) touche le cœur du cycle de run (composition + état last-run) — fork de
  design (cron vs HH:mm local). L'interim « désactiver le champ Planification » est entremêlé dans la boucle
  de champs générique du wizard + relève d'un choix UX. → **session dédiée** ; décider d'abord le parseur cron.

## 5. RB6 — horodatages au fuseau du navigateur (EN COURS — infra + P0 livrés)
- [x] **Infra** : `IBrowserTimeZone` (scopé/circuit, JS `liakontTime.getTimeZone`, fallback UTC) +
      `LiakontDateDisplay` (helper FR, UTC→local, repli UTC suffixé) + composant `<LiakontDate>` +
      sonde `<BrowserTimeProbe>` dans le shell (résout 1×/circuit, événement → re-rendu). JS Liakont
      (hors socle). DI AddScoped. Tests : 20 (xUnit helper/service + bUnit LiakontDate incl. re-rendu).
- [x] **P0** (9 sites, le bug visible) migrés vers `<LiakontDate>` : Agents, AgentStatusList, Treatments,
      Clients, Supervision, SupervisionDetail, TableTvaView, ReconciliationView, Documents (LastUpdateUtc).
      NB : `Documents.IssueDate` = DateOnly (sans fuseau) → laissé tel quel (le convertir serait un bug).
      Les 10 tests bUnit de pages migrées câblés (`AddBrowserTimeZoneStub`) → 961/961.
- [ ] **P1** (4 sites Host à uniformiser : Flotte, Signatures, DocumentDetailView, SupervisionLivenessBanner).
- [ ] **P2** (~20 pages modules : Audit/Identity/Notification/Job). NB : sites cron/planif (AdminJobScheduleForm,
      AdminJobExecutions) → garder UTC EXPLICITE (prévision serveur, pas un horodatage d'événement).

## Notes
- Démo cette nuit/demain : PA = **Fake** (Development) pour exercer agent→plateforme→PA de bout en bout.
- Ne PAS `demo.ps1 reset` (SEM Keroman en cours de démo).
- L'ancien contenu (SIG07) était une session orchestration terminée — archivé dans git.
