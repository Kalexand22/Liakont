# Provisioning de tenant — OPS03 en interactif (feat/tenant-provisioning) — CHANTIER CODÉ

Plan approuvé : C:\Users\K_ale\.claude\plans\happy-sauteeing-lovelace.md
Décisions opérateur : package agent = clé + installeur générique ; ordre A → B → C.

## État : les 3 lots sont DONE (commits locaux, non poussés — recette opérateur d'abord)
- Lot A+B (commit 1) : provisioning d'utilisateur de tenant (IKeycloakUserProvisioner socle +
  ITenantUserProvisioningService Host + POST /admin/tenants/{id}/users ; company_id généré au
  provisioning, persisté au registre, mapper realm HARDCODÉ ; mdp admin aléatoire remis une fois)
  + application du statut Suspendu (refus push agent 403 FR sans perte, heartbeat servi ; refus
  sign-in + middleware sessions/API ; super-admin jamais bloqué ; page /tenant-suspendu).
- Lot C (commit 2) : écran /clients (liste composée, suspension/réactivation avec confirmation)
  + assistant /clients/nouveau (profil → seed optionnel → création idempotente avec reprise →
  premier utilisateur sautable → premier agent clé-une-fois + installeur générique → récap) ;
  nav Supervision en sous-menu {Vue d'ensemble, Clients} ; garde companyId create-only partagée.
- Vérifications par lot : verify-fast PASS, run-tests PASS (5164-5236), codex CLEAN (R3 lots A+B,
  R5 lot C). Provenance socle §4.24/4.25 + baseline régénérée.

## Reste à faire (opérateur)
- [ ] RECETTE VISUELLE : serveur sur http://localhost:55996 (compte superviseur requis pour voir
      Supervision → Clients ; le realm dev projette sysadmin → vérifier ses rôles realm).
      Parcours : liste Clients, assistant complet (créer un tenant de test RÉEL — attention :
      Keycloak admin local doit être configuré pour le realm, sinon création base+registre seule),
      suspension/réactivation + effet sur push agent.
- [ ] PUSH de feat/tenant-provisioning après recette (2 commits locaux).
- [ ] MANIFEST (geste opérateur) : OPS03 traité hors orchestration → marquer done (ou re-trancher) ;
      OPS06a et GATE_TOOLKIT dépendent d'OPS03.

## Suivis signalés (hors périmètre, à trancher/planifier par l'opérateur)
- Jobs pipeline (SEND/CHECK fan-out) traitent encore les tenants SUSPENDUS — incohérence à acter
  (un tenant suspendu ne devrait probablement plus émettre ; item dédié).
- Realm E2E sans company_id (mapper hardcodé à poser dans keycloak-e2e-realm.json) — débloquerait
  les E2E de pages de données ; item dédié.
- Gestion continue utilisateurs/rôles : spec OPS03 §3 (« écrans Identity du socle en nav »)
  CONTREDIT la décision FIX209/E5 (sections socle retirées, pages non opérantes sous OIDC).
  Tranché ici : non référencés ; le provisioning du lot A couvre la création — le reste à décider.
- E2E assistant borné (création réelle infaisable dans la factory E2E : pas de SystemAdmin système
  ni Keycloak admin) — couvert par l'intégration ; à étendre si la factory évolue.
- Dette console-polish toujours ouverte : « € » en dur sans BT-5 dans le read-model.
