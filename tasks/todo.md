# TODO — Session autonome nuit 2026-06-17 (Karl dort, « tout fix à la suite »)

Branche : `feat/emitter-filled-by-platform`. Règle : chaque item = verify-fast + tests + review +
commit+push (commit ⇒ push). Ordre validé par Karl : RB9 → **SuperPDP (option 1, prioritaire)** →
RB7 → RB8 → RB6.

## 1. RB9 — clore les 3 P2 de review (EN COURS)
- [ ] P2.1 — garde SEND anti-« envoi faux » : `StagedReadStatus.EmitterUnresolved` ; si l'émetteur
      reste nul après enrichissement read-time → HOLD (ne pas transmettre/archiver), log opérateur FR.
- [ ] P2.3 — passer `companyId` en paramètre à `ReadStagedPivotAsync` (supprimer le
      `GetCurrentCompanyId()` redondant par document).
- [ ] P2.2 — tests de régression : (a) `payloadHash` d'ingestion INVARIANT au profil tenant (lock RB9) ;
      (b) CHECK et SEND remplissent réellement l'émetteur (double profil-renseigné), 389 non écrasé.
- [ ] verify-fast + run-tests + codex-review (round loop) → commit+push.

## 2. SuperPDP — option 1 (PRIORITAIRE, item suivant) — voir RB10 dans recette-demo-bucodi.md
- [ ] Modèle compte PA + migration : champs OAuth2 (`client_id` + `client_secret`) chiffrés par tenant.
- [ ] Mode d'auth déclaré par le plug-in (capacité) ; form PA conditionnel (clé API vs OAuth2).
- [ ] `ISuperPdpAccountResolver` Host (déchiffre via coffre TenantSettings).
- [ ] Host : ProjectReference SuperPdp + `AddSuperPdpPaClient()` au composition root.
- [ ] Tests + verify + review → commit+push. (SuperPDP = Sandbox only, BaseUrl lève en Prod, F14 §12 O1.)

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
