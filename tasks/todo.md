# RLM01 — Realm Keycloak unique partagé (étape 1 du séquencement ADR-0021)

Branche : `feat/tenant-provisioning-RLM01` (sous-branche de `feat/tenant-provisioning`, segment `realm-unique`).
Spec : ADR-0021 §1-2-3-5 + invariants INV-0021-2/3/6/7/8/10. Item : orchestration/items/RLM.yaml (RLM01).

## Principe directeur (anti faux-vert)
Toute config realm s'applique aux **DEUX** fichiers, jamais un seul :
- `deploy/docker/keycloak/realm-export.json` (DEV)
- `tests/Liakont.Tests.E2E/Fixtures/keycloak-e2e-realm.json` (E2E = vrai chemin de login testé)

## État existant (cartographié)
- DEV realm : mapper `company_id` HARDCODÉ niveau client (`oidc-hardcoded-claim-mapper`, valeur fixe) → à RETIRER. `registrationAllowed=false`, `loginWithEmailAllowed=true`, `duplicateEmailsAllowed=false` déjà OK. Users : lecture/operateur/parametrage/superviseur/exploitant/sysadmin, AUCUN attribut `company_id`. Pas d'otpPolicy / 2FA. Pas de userProfile déclaratif.
- E2E realm : mapper `company_id` déjà en `oidc-usermodel-attribute-mapper`. `registrationAllowed=false`. 4 users (lecture/operateur/parametrage/superviseur) SANS attribut `company_id`. Pas d'otpPolicy. Pas de userProfile.
- GUID tenant `default` = `00000000-0000-4000-a000-000000000001` (= DevTenantSeedOptions.CompanyId, à garder cohérent).
- `KeycloakRealmProvisioner.BuildHardcodedMapper` (code provisioning) + son test → HORS scope RLM01 (recadrage = RLM04). RLM01 ne touche QUE realm-export.json, pas ce code.

## Tâches
- [x] D1 — DEV realm-export.json : mapper hardcodé `company_id` -> mapper d'ATTRIBUT (identique E2E). Validé (parse).
- [x] D2 — Backfill attribut `company_id` (= GUID default ...-001) sur lecture/operateur/parametrage/superviseur/exploitant (DEV) + les 4 users (E2E). sysadmin SANS company_id.
- [x] D3 — 2e tenant : user `tenant2` (company_id ...-002) dans les 2 realms + entrée `outbox.tenants` via DevTenantSeeder (AdditionalTenants config). default reste NULL (RLM02).
- [x] D4 — userProfile déclaratif (composant Keycloak 26 `declarative-user-profile`) : `company_id` edit=[admin] (read-only user, INV-0021-3) sur les deux realms. Parse interne validé.
- [x] D5 — 2FA : otpPolicy (TOTP/HmacSHA1/6/30) sur les 2 realms ; DEV = `requiredActions:[CONFIGURE_TOTP]` (enrôlement forcé) ; E2E = credentials OTP pré-enrôlés + `TotpGenerator` (RFC 6238, test unitaire) + `KeycloakLoginPage.FillOtpAsync`/`LoginViaKeycloakAsync` calcule le code.
- [x] D6 — email obligatoire/unique : loginWithEmailAllowed=true, duplicateEmailsAllowed=false (les 2 realms) ; asserté T1.
- [x] D7 — registrationAllowed=false : asserté sur les DEUX realms (T1).
- [x] T1 — Tests structurels (run-tests) parsant les 2 realms : `SharedRealmConfigTests` (7 vérifs ×2 realms + INV-0021-2a négatif + 2e tenant distinct) + `DevAdditionalTenantsConfigTests` (donnée de seed registre) + `TotpGeneratorTests` (vecteurs RFC, NON E2E). 32 tests verts.
- [~] T2 — Tests E2E (Category=E2E) : le chemin de login E2E existant traverse désormais le 2FA (helper maj) → validé à la GATE. La preuve d'isolation complète (cross-check) dépend de RLM03 ; pas de nouveau test E2E d'isolation en RLM01 (prématuré).
- [x] V — verify-fast vert (plateforme net10 + agent net48 x86/x64 + analyzers + unit-tests). run-tests + codex-review : en cours.

## DÉCISIONS (tranchées — session de reprise 2026-06-13)
- DÉC-1 (périmètre backfill) — **TRANCHÉE : suivre la spec « dev : 5 rôles »**. `SuperAdminRoles` =
  {Admin, SystemAdmin, stratum-admin} UNIQUEMENT. `exploitant`→`liakont.fleet` n'est PAS super-admin →
  sous le cross-check RLM03 fail-closed, un exploitant sans company_id serait 403. Donc DEV backfille
  lecture/operateur/parametrage/superviseur/**exploitant** ; SEUL `sysadmin` (stratum-admin) exempté.
  E2E backfille les 4 users. (Corrige la DÉC-1 initiale qui excluait exploitant — contredisait la spec
  ET aurait cassé l'accès exploitant sous RLM03.) Test négatif INV-0021-2a dérive « super-admin » de
  stratum-admin/Admin/SystemAdmin.
- DÉC-2 (2FA <-> E2E) : pré-enrôler TOTP (credential otp, secret connu) sur les users E2E + 2e tenant ;
  TotpGenerator (RFC 6238, HMACSHA1, base32) + test unitaire (vecteurs RFC, NON E2E → tourne en run-tests) ;
  KeycloakLoginPage.FillOtpAsync (#otp) + LoginViaKeycloakAsync calcule le code. DEV : CONFIGURE_TOTP forcé
  (enrôlement au 1er login), pas de pré-enrôlement.
- DÉC-3 (split test gate) : preuves E2E (vrai mapper / 2FA / claims mdp / isolation 2e tenant) -> GATE
  humaine (Category=E2E, hors run-tests) ; structurel (parse des 2 realms + math TOTP) -> run-tests.
- DÉC-4 (2e tenant) : company_id `00000000-0000-4000-a000-000000000002` (distinct du default ...-001) ;
  1 user `tenant2` (rôle lecture) dans les 2 realms ; entrée outbox.tenants via DevTenantSeeder
  (AdditionalTenants config), database_name neuve (auto-créée par EnsureDatabase, skip non-fatal),
  realm_name placeholder distinct (vestigial, retiré en RLM04). default reste company_id=NULL (backfill RLM02).
- DÉC-5 (périmètre realms) — **Keycloak 26.0**. RLM01 = DEV (realm-export.json) + E2E (keycloak-e2e-realm.json)
  seulement. `realm-liakont.json` (prod appliance) utilise DÉJÀ le mapper d'attribut company_id (pas de bug
  D1) et est géré séparément (users réels provisionnés) → HORS périmètre explicite (anti scope-creep).

## Review
- verify-fast : PASS (plateforme net10 + agent net48 x86/x64 + analyzers + unit-tests).
- run-tests : PASS (5547 tests, 3 suites, 0 échec ; self-test provisioning 27 OK).
- codex-review R1 (vs feat/tenant-provisioning) : 0 P1, 3 P2.
  - P2#1 (otpPolicyCodeReusable=false → E2E flaky par réutilisation du code dans la même fenêtre 30 s) :
    CORRIGÉ — `otpPolicyCodeReusable: true` dans le realm E2E SEULEMENT (DEV reste false, durci).
  - P2#2 (secret TOTP dupliqué realm↔E2EUserOtpSecrets sans test de concordance = faux-vert) :
    CORRIGÉ — `E2EUserOtpSecretsConsistencyTests` (run-tests) asserte la concordance par user.
  - P2#3 (clé HMAC = ASCII brut du secret Keycloak, non prouvable sans Keycloak) : ACCEPTÉ —
    le reviewer demande explicitement « pas de correctif code ; tracer comme critère de GATE ».
    **Critère de GATE_REALM_UNIQUE : valider l'hypothèse ASCII-brut au 1er login E2E réel (1 login suffit).**
- codex-review R2 : 0 P1, 1 P2 actionnable + 1 P2 différé.
  - P2 (test-hole SharedRealmConfigTests : prouvait le type du mapper mais pas l'émission du claim) :
    CORRIGÉ — assertions `id.token.claim=="true"` et `access.token.claim=="true"` ajoutées.
  - P2 différé (realm prod realm-liakont.json sans otpPolicy/userProfile) : ACCEPTÉ — hors périmètre RLM01
    (DÉC-5, prod gérée séparément, mapper d'attribut déjà correct ; aucune régression). **Suivi : item RLM
    aval doit poser 2FA + user-profile immuable sur le realm prod** (sinon INV-0021-7 only en dev/E2E).
- codex-review R3 : à confirmer clean.
