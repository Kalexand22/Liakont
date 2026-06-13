# Provisioning de tenant — OPS03 en interactif (feat/tenant-provisioning)

Plan approuvé (2026-06-12) : C:\Users\K_ale\.claude\plans\happy-sauteeing-lovelace.md
Décisions opérateur : package agent = clé + installeur générique ; ordre A → B → C.
L'opérateur s'absente — dev autonome, recette visuelle à son retour, push après recette.

## Lots A+B — DONE (commit local sur feat/tenant-provisioning, codex CLEAN R3, run-tests 5236 verts)

## Lot A — Provisioning d'utilisateur dans un realm existant
- [ ] Socle (provenance §4 OBLIGATOIRE par modif) : KeycloakRealmProvisionRequest.CompanyId ;
      KeycloakRealmProvisioner (mapper company_id hardcodé + UPDATE_PASSWORD/temporary=true) ;
      TenantProvisioningService (companyId généré+persisté, mdp admin aléatoire retourné une fois) ;
      V016 outbox.tenants.company_id (+ SystemOnlyMigrationPrefixes) ; TenantDto/TenantQueries ;
      seam IKeycloakUserProvisioner + impl + enregistrement DI.
- [ ] Host : ITenantUserProvisioningService + LiakontRealmRoles + KeycloakTenantUserProvisioner
      (compensation, invitation email queue système, mdp une fois si SMTP absent) ;
      POST /admin/tenants/{id}/users ; HandleSeedAsync repli/garde companyId ; AppBootstrap.
- [ ] Tests : unit provisioner Host + unit socle (payload admin, mapper) + intégration endpoint users.
- [ ] verify-fast + run-tests + codex CLEAN + commit lot A.

## Lot B — Application du statut Suspendu
- [ ] GetCurrentTenantStatut (Contracts+Postgres TenantSettings).
- [ ] ITenantSuspensionLookup (singleton, cache 30 s, fail-open documenté).
- [ ] TenantSuspendedPushFilter sur les 3 endpoints d'écriture agent (403 FR ; heartbeat servi).
- [ ] TenantSuspensionMiddleware (API 403 / UI signout+redirect ; SuperAdmin jamais bloqué) +
      page anonyme /tenant-suspendu + refus au sign-in OIDC (OnTokenValidated).
- [ ] Tests : unit lookup+middleware ; intégration push 403 + console 403 + réactivation ; bUnit page.
- [ ] verify-fast + run-tests + codex CLEAN + commit lot B.

## Lot C — Écran « Clients » + assistant
- [ ] TenantSettings : CompanyId? sur SetTenantStatusCommand + SaveTenantProfileCommand (garde) ;
      ResolveCompanyId : override honoré si tenant SANS profil + test dédié.
- [ ] Host Clients/ : IClientConsoleService + impl (liste composée, échec visible ; create in-process ;
      seed/profil in-scope ; 1er user lot A ; 1er agent PIV05 clé une fois ; statut), Line/Registry/
      WizardState/ActionStatus.
- [ ] UI : /clients (liste + suspension) ; /clients/nouveau (stepper 4 étapes, vues pures, échecs
      visibles + retry, AlreadyProvisioned = reprise) ; nav branche Supervision ; config
      AgentInstaller:DownloadUrl + TenantSeeds:RootPath ; doc identity-permissions.
- [ ] Tests : bUnit (pages+vues+service+nav) ; intégration provisioning complet+isolation+garde ;
      E2E assistant borné (realm E2E sans company_id).
- [ ] verify-fast + run-tests + codex CLEAN + commit lot C.

## Fin de chantier
- [ ] Synthèse opérateur : recette à faire, suivis (jobs pipeline tenants suspendus ; realm E2E
      company_id ; gestion continue Identity vs FIX209 ; manifest OPS03/OPS06a/GATE_TOOLKIT ;
      dette devise BT-5). Push après recette opérateur.
