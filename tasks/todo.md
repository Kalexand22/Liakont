# RLM04 — Sortie du provisioner realm (seam prouvé) + recadrage mapper hardcodé + provenance + nettoyage vestigial

Segment realm-unique (feat/tenant-provisioning), ADR-0021 §1/§5. Sous-branche feat/tenant-provisioning-RLM04.

## Décisions d'architecture (tranchées)
- **Seam = no-op DI** (ADR §1, option « no-op enregistrée en DI ») : `NoOpKeycloakRealmProvisioner`
  enregistré par défaut (profil SaaS partagé) ; le vrai `KeycloakRealmProvisioner` n'est résolu que
  si `Keycloak:DedicatedRealmPerTenant=true` (profil dédié mono-tenant — garde la capacité socle).
- **Nettoyage vestigial = gate sur `realmCreated`** : dans `ProvisionAsync`, `RegisterRealm` +
  `AddTenantRedirectUriAsync` ne s'exécutent QUE si un realm a réellement été créé
  (`!AlreadyProvisioned`). Le no-op renvoie `Idempotent` → `realmCreated=false` → ni registre ni
  redirect par tenant en partagé. Le redirect statique `default.localhost` (realm-export.json) n'est
  PAS touché (FIX07a respecté).
- **Mapper hardcodé** : pas de changement de prod (le vrai provisioner reste correct POUR LE DÉDIÉ) ;
  on RECADRE le test `Should_Emit_CompanyId_As_Hardcoded_Client_Mapper` au profil dédié (+ commentaire
  « jamais pour le partagé ») et on met à jour provenance §4.24.
- **E2E de clôture** : le cross-check RLM03 a rendu le login `lecture` (tenant `default`) LATENT-cassé
  en E2E (outbox.tenants vide → 403). Le harness E2E seede `default` (-001) ET `tenant2` (-002) dans
  outbox.tenants ; `tenant2` reçoit sa propre base migrée (db/realm/company_id UNIQUE imposé par V008/
  V010/V017). Nouveau test : un utilisateur de `tenant2` se connecte de bout en bout dans le realm
  partagé et atteint le shell (exerce RLM01→RLM03).

## Plan (checkable)

### A — Seam (no realm provisionné en partagé)
- [x] A1. `NoOpKeycloakRealmProvisioner.cs` (NOUVEAU)
- [x] A2. `ServiceCollectionExtensions.cs` : no-op par défaut ; vrai si `DedicatedRealmPerTenant`
- [x] A3. `TenantProvisioningService.ProvisionAsync` : `RegisterRealm`+`AddTenantRedirectUri` sous `if (realmCreated)`
- [x] A4. Tests unitaires : DI résout no-op/vrai ; no-op = 0 HTTP + AlreadyProvisioned

### B — Recadrage mapper hardcodé
- [x] B1. `KeycloakRealmProvisionerTests` : test hardcodé renommé `…DedicatedProfile…` + commentaire
- [x] B2. provenance §4.24 : note recadrage RLM04

### C — E2E de clôture
- [x] C1. `KeycloakE2EWebFactory` : seed outbox.tenants (default -001) + tenant2 (db propre, -002) + TenantConnections:tenant2
- [x] C2. `Scenarios/TenantLoginSharedRealmE2ETests` : tenant2 se connecte → shell

### D — Nettoyage vestigial (Host, hors socle)
- [x] D1. `AppBootstrap.SeedRealmRegistryFromDatabaseAsync` : boucle par-tenant seulement si `DedicatedRealmPerTenant`

### E — Provenance + vérif
- [x] E1. provenance §4.28 + bloc SOCLE-CONSIGNED-DRIFT (+ ServiceCollectionExtensions.cs)
- [x] E2. verify-fast PASS (socle-provenance-check vert)
- [x] E3. run-tests PASS (5589 tests) ; E2E login EXÉCUTÉ localement (run-e2e -Filter Login : 2/2 PASS — LoginShell + TenantLoginSharedRealm)
- [ ] E4. codex-review CLEAN / P2 accepté
      - R1 : 2 P2 (régression realmCreated ; consommation non testée) → corrigés (garde sur Authority ; test d'intégration Testcontainers)
      - R2 : 0 P1, 2 P2 → P2#1 (E2E non exécuté) RÉSOLU (E2E exécuté 2/2) ; P2#2 (provisioning utilisateur en realm partagé, hors périmètre seam) = dette consignée §4.28 + tâche de suivi

## Review (rempli en fin de session)
