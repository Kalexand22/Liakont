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

## 3. RB7 — le wizard d'install démarre le service après install (`AgentProcessDeployer`, OPS08b)
- [ ] `sc start` après install + check-config OK ; refléter *Running* dans l'écran Résumé. Tests + push.

## 4. RB8 — la planification est réellement appliquée (fin de la cadence 1 min figée, AGT03)
- [ ] Le runner consulte `EffectiveExtractionPlan` (HH:mm local / cron plateforme) pour gater les runs.
- [ ] Tests + push. (Sinon : masquer/désactiver le champ « Planification » du wizard — false affordance.)

## 5. RB6 — horodatages au fuseau du navigateur (helper commun UI, socle vendored non modifié)
- [ ] Helper commun de formatage date/heure (JS interop offset navigateur, résolu une fois par circuit).
- [ ] Généraliser (pas que la page Agents). Tests bUnit. + push.

## Notes
- Démo cette nuit/demain : PA = **Fake** (Development) pour exercer agent→plateforme→PA de bout en bout.
- Ne PAS `demo.ps1 reset` (SEM Keroman en cours de démo).
- L'ancien contenu (SIG07) était une session orchestration terminée — archivé dans git.
