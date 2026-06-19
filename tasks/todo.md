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
- [x] **Slice 2 — stockage chiffré OAuth** (LIVRÉ, commit efbfa4c) : migration V011 (encrypted_client_id/secret),
      PaAccount (+2 champs+setters), Add/UpdatePaAccountCommand (+ClientId/ClientSecret), handlers (Protect par
      purpose DÉDIÉ — PaAccountSecretPurposes), UoW (INSERT/UPDATE/SELECT/Reconstitute), PaAccountDto
      (+HasClientId/HasClientSecret), queries (IS NOT NULL). ISecretProtector : surcharges (plaintext, purpose).
- [x] **Slice 3 — câblage + résolveur** (LIVRÉ, commit efbfa4c) : IPaAccountSecretStore + PostgresPaAccountSecretStore ;
      SuperPdpAccountResolver (Host, scope tenant via ITenantScopeFactory, déchiffre, mappe l'env, bloque si absent) ;
      Host csproj ref SuperPdp + AddSuperPdpPaClient() + TryAddSingleton resolver (composition root, inconditionnel).
      → SuperPDP apparaît dans la liste.
- [x] **Slice 4 — formulaire** (LIVRÉ, commit efbfa4c) : ComptesPaView conditionnel au AuthMode (clé API vs
      client_id/secret type=password, accountId requis pour OAuth) + PaAccountConsoleService/Model (AuthModes) + bUnit.
- [x] verify + run-tests + codex-review + build Release : tous verts (commit efbfa4c). (SuperPDP = Sandbox only,
      BaseUrl lève en Prod, F14 §12 O1.)
- **RESTE (suivi)** : recette Karl = créer un compte SuperPDP OAuth2 dans la console puis envoi sandbox réel.
  SuperPDP option 1 = TERMINÉ côté code (slices 1-4).

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
- [x] **P1** (Host) : Flotte (backup/last-seen) + Signatures (OccurredAt) migrés vers `<LiakontDate>`.
      **Laissés en UTC À DESSEIN** (pas le bug, documenté) : DocumentDetailView (timeline d'événement en
      UTC ISO pour CONTRÔLE FISCAL — recoupe écran/export, CLAUDE.md n°2) ; SupervisionLivenessBanner (UTC
      ÉTIQUETÉ, diagnostic dead-man's-switch avec intervalle serveur). Signatures.échéance = DateOnly→minuit-UTC,
      non convertie (comme IssueDate). FlotteTests/SignaturesTests recâblés.
- [ ] **P2 — BLOQUÉ sur décision d'archi** : les pages `Modules.*.Web` (Audit/Identity/Notification/Job) ne
      référencent PAS `Liakont.Host` (dépendance inverse) → `<LiakontDate>` (dans le Host) n'y est pas
      disponible. Pour P2 il faut DÉPLACER l'infra RB6 (IBrowserTimeZone + LiakontDateDisplay + LiakontDate +
      le JS) dans une **RCL Liakont partagée** (nouveau projet, ou lib commune Liakont hors socle) référencée
      par le Host ET les modules.Web. = vraie restructuration → à acter avec Karl. NB : sites cron/planif
      (AdminJobScheduleForm, AdminJobExecutions) restent UTC EXPLICITE (prévision serveur, pas un événement).

## 6. Chantier 2 — Adaptateur Chorus Pro (tranche démo) — plan détaillé : plan-chorus-pro.md
- [ ] Plan complet rédigé (CP1→CP9 + prérequis externes + DoD). **À relire par Karl.**
- Décisions tranchées : D1 = **étendre le schéma** (3ᵉ secret compte technique) ; D2 = **e-reporting
  exclu du plug-in** (sourcé : Chorus Pro = B2G, e-reporting B2B via PA/PDP).
- **Dépendance** : SuperPDP option-1 « Partie A » (slices 2-4 ci-dessus, **⛔ NON COMMENCÉE** — vérifié :
  TenantSettings=V010, pas de V011/encrypted_client_id/IPaAccountSecretStore ; seule slice 1 mergée) =
  infra OAuth générique partagée. **Bloque CP5/CP6** ; CP1→CP4 (plug-in pur) codables en parallèle.
  Brancher Partie A ↔ Chorus Pro sur une branche partagée à décider (B5).
- Plan **redliné par Karl 2026-06-18** : corrections de fond intégrées (dépôt→Sending jamais Issued ;
  endpoints *.piste.gouv.fr ; pas d'idempotence par numeroFluxDepot ; libellés consulterCR exacts ;
  câblage via bootstrap dédié, pas AddConfiguredPaClients).
- **Arbitrages §2bis tranchés** : D7 = nouvel item plateforme **PR-PIPE** (support PaSendState.Sending
  async : persiste PaDocumentId, confirme via consulterCR, finalise Issued seulement sur Intégré, ne
  re-dépose jamais — bénéficie à SuperPDP ; prérequis envoi prod/e2e) ; D8 = pas de re-POST auto ;
  D9 = avecSignature=false (Factur-X non signé) ; C3 = idUtilisateurCourant via consulterCompteUtilisateur
  +cache ; B5 = Partie A mergée sur main d'abord, puis brancher feat/pa-chorus-pro.
- Chorus Pro = hybride **SuperPdp** (OAuth2 client_credentials/PISTE) + **Generique** (transport
  Factur-X scellé) ; sera le 1er connecteur HTTP réellement câblé en prod.

## Notes
- Démo cette nuit/demain : PA = **Fake** (Development) pour exercer agent→plateforme→PA de bout en bout.
- Ne PAS `demo.ps1 reset` (SEM Keroman en cours de démo).
- L'ancien contenu (SIG07) était une session orchestration terminée — archivé dans git.
