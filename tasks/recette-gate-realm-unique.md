# Recette manuelle — GATE_REALM_UNIQUE (ADR-0021)

Légende : ✅ passé · ❌ KO (finding) · ☐ non déroulé · 🔁 à re-tester

Environnement (bring-up 2026-06-14) : Keycloak realm unique `liakont-dev` réimporté propre (reset),
base app `liakont` neuve sur :5432 (5432 libéré d'un Postgres WSL squatteur), Host `dotnet run` :55996.
Comptes realm = mot de passe `Test@1234`. Tenants seedés : `default` (company_id …-001),
`tenant2` (company_id …-002), `sysadmin` (super-admin, sans company_id).

## RUN 1 (2026-06-14→15) — ✅ VALIDÉE après correctifs F1/F2/F6

### Acceptance gate — les 4 critères prouvés EN RÉEL
1. ✅ User de tenant connecté **de bout en bout** dans le realm partagé : tenant `test2` provisionné via
   l'assistant `/clients/nouveau`, utilisateur créé dans `liakont-dev` (realm PARTAGÉ) avec
   `company_id=378f4241…`, connexion réussie. Clôt la panne OPS03. (Débloqué par **F2**.)
2. ✅ Enrôlement **2FA** effectif : utilisateur provisionné TENU à l'écran `CONFIGURE_TOTP` (impossible
   à contourner) ; credential `otp` posé après enrôlement. (Débloqué par **F6**.)
3. ✅ **Cross-check** bloque le cross-tenant : session `test2` + `X-Tenant-Id: default`/`tenant2` → **403** ;
   sans indice ou indice cohérent (`test2`) → **200** (servi pour la bonne raison). Isolation visuelle
   confirmée en parallèle (option b).
4. ✅ Provisioning **sans realm dédié** : aucun realm `stratum-test2` (GET → 404), utilisateur dans le
   realm partagé `liakont-dev`.

**Verdict recette : les 4 critères fonctionnels sont prouvés.** Reste administratif (acceptance gate) :
committer F1/F2/F6 → PR → CI verte (verify + tests) → **merge humain**. F3/F4/F5/F6-A consignés (suivi séparé).

## Findings

### [P1] F2 — Création d'utilisateur de tenant impossible en realm partagé (bloqueur de gate)
- **Symptôme** : assistant « Nouveau client » → étape Utilisateur → « rien » à l'écran.
  Logs : `GET /admin/realms/stratum-moi/users → 404 "Realm not found"` puis exception non gérée
  (circuit Blazor) dans `KeycloakUserProvisioner.FindUserIdByUsernameAsync`.
- **Cause racine** : `KeycloakTenantUserProvisioner.ProvisionUserAsync` crée l'utilisateur dans
  `tenant.RealmName` = `"stratum-{id}"` (stocké par `TenantProvisioningService.ProvisionAsync`,
  toujours codé en dur), alors qu'en profil SaaS PARTAGÉ (RLM04, no-op DI) **aucun realm dédié n'est
  créé** → realm inexistant → 404. L'ADR-0021 §1 exige que **tous les utilisateurs vivent dans le
  realm partagé** (`liakont-dev`). Le pivot realm-unique n'a jamais recâblé la création d'UTILISATEUR
  vers le realm partagé.
- **Faux-vert associé (règle review #8)** : `KeycloakTenantUserProvisionerTests` passe au vert car son
  `FakeKeycloakUserProvisioner` **ignore le `realmName`** → le mauvais realm n'est jamais asserté.
- **Fix** : `KeycloakTenantUserProvisioner` cible le realm partagé (`Keycloak:PrimaryRealmName`) en
  profil partagé, `tenant.RealmName` en profil dédié (`Keycloak:DedicatedRealmPerTenant=true`). + test
  qui assère le realm cible (anti-faux-vert).

### [P2] F1 — Assistant « Nouveau client » : cul-de-sac sur SIREN invalide
- **Symptôme** : SIREN passant la forme (9 chiffres) mais invalide (clé de Luhn, INV-TENANTSETTINGS-001
  validé serveur à l'étape Création) → erreur affichée, mais **aucun moyen de revenir en arrière** pour
  corriger (seul « Réessayer », qui rejoue le même SIREN).
- **Fix** : navigation arrière entre les étapes (boutons « Retour » par étape), données préservées,
  identifiant technique verrouillé une fois le tenant créé (reprise idempotente). La validation Luhn
  reste au domaine (pas de règle dupliquée).

### [P2] F3 — `tenant2` de dev cassé (consigné, hors périmètre du fix immédiat)
- `outbox.tenants.database_name = "liakont_tenant2"` mais le runtime dérive `{DatabasePrefix=stratum_}`
  → `stratum_tenant2` (jamais créée). Login `tenant2` OK (tenant résolu) mais toute requête base plante
  (dashboard, suspension, langue : `3D000 database "stratum_tenant2" does not exist`).
- Origine : `appsettings.Development.json:DevTenantSeed.AdditionalTenants[0].DatabaseName`.

### [P2] F4 — Faux-vert migration tenants (consigné, hors périmètre du fix immédiat)
- `MigrateExistingTenantsAsync` logge « 2/2 migrés » au démarrage alors que la base de `tenant2`
  n'existe pas (ni `liakont_tenant2` ni `stratum_tenant2`). Une base de tenant injoignable est
  silencieusement « skippée » sans baisser le compteur ni alerter. Socle vendored.

### [P2] F5a — Entrées de nav opérateur non gardées par permission (consigné, à corriger)
- `LiakontNavNodeProvider` ajoute **Documents / Encaissements / Traitements / Paramétrage** SANS
  garde de permission (contrairement à Supervision/Flotte, gardées). Un utilisateur `lecture`
  (lecture seule) voit donc « Traitements » (besoin `liakont.actions`) et « Paramétrage »
  (besoin `liakont.settings`) qu'il ne peut pas utiliser → incohérence matrice §3.
- Les pages elles-mêmes ne portent que `[Authorize]` (aucune policy) → ouvrables par tout
  authentifié (le contenu/actions est gardé, mais l'entrée + la page le sont insuffisamment).
- Fix attendu : gater ces entrées (et poser la policy sur les pages) par la permission §3, comme
  Supervision/Flotte. Le super-admin garde l'accès large via le court-circuit `HasPermission`.

### [DESIGN] F5b — Super-admin agissant cross-tenant sur les documents (intuition Karl)
- Faisabilité actuelle : NULLE par défaut. Le tenant courant vient du claim `company_id`
  (`CompanyClaimTenantResolver`, voie autoritaire) ; le super-admin n'en a pas → aucun tenant
  résolu → « Documents » est un cul-de-sac. « Agir sur tous les documents » = fonctionnalité à
  construire (sélecteur/impersonation de tenant pour le super-admin).
- Audit : la fondation existe. `DocumentEvent` (append-only, triggers base) capture
  `OperatorIdentity` + `OperatorName` figés à l'instant de l'action ; les actions manuelles
  EXIGENT l'identité. Une action super-admin serait donc tracée nominativement.
- Décision : si on ouvre l'action cross-tenant au super-admin, le faire via un sélecteur de tenant
  explicite + s'appuyer sur cet audit. Item séparé, hors gate realm-unique.

### [P1] F6 — 2FA non imposé au login mot de passe sur le chemin de provisioning (CORRIGÉ — B)
- Observé : `test2` (user provisionné) a un credential `password` mais AUCUN `otp`, `requiredActions`
  vide → connecté SANS 2FA. Le realm-export n'a ni `requiredActions` par défaut, ni flow browser
  custom, ni `browserFlow` défini → Keycloak applique le flow par défaut (OTP CONDITIONNEL, sauté si
  l'user n'a pas d'OTP). Le « 2FA forcé » ne tient qu'au `CONFIGURE_TOTP` **seedé par compte** de démo.
- Faux-vert : l'E2E de login (RLM01) « prouve » le 2FA via des comptes pré-seedés porteurs de
  `CONFIGURE_TOTP` ; le vrai chemin de provisioning l'esquive. Acceptance ② de la gate KO + contrôle
  de sécurité non appliqué.
- **Fix B (livré)** : `KeycloakTenantUserProvisioner` ajoute `CONFIGURE_TOTP` aux `RequiredActions`
  (avec `UPDATE_PASSWORD`) → tout user provisionné enrôle son 2FA à la 1re connexion. + test.
- **F6-A (durcissement, consigné)** : enforcement realm-level — `CONFIGURE_TOTP` en action PAR DÉFAUT
  du realm (dev + E2E, principe « deux realms ») + preuve de config statique. Couvre TOUT chemin de
  création (console admin, brokered RLM05), pas seulement le provisioner. Item séparé.

## Périmètre du fix RUN 1 (décision Karl)
Les 2 bugs (F2 + F1) + tests anti-faux-vert. F3/F4/F5 consignés pour traitement séparé (décision :
finir la gate d'abord). Pas de modification du socle vendored.
