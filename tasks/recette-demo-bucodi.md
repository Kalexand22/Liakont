# Recette — Environnement de démo Bucodi (capture des observations)

> Observations dictées par Karl pendant la recette de l'env de démo (`deployments/bucodi`, branche
> `demo/bucodi`). **À convertir en items d'orchestration en fin de session. NE PAS corriger pendant
> la recette** (capture seulement).

Session : 2026-06-17. Env : `liakont-bucodi` (Host Development, realm `bucodi`).

## État de résolution (corrigés en interactif, vérifiés en live)

| # | Statut | Commit | Vérification |
|---|---|---|---|
| RB1 | ✅ Résolu | `33f0d55` | login headless super-admin : nav tenant masquée, chip absent, `/` → `/supervision` |
| RB2 | ✅ Résolu | `0acf097` | reset → `outbox.tenants` vide (0 tenant) ; super-admin → `/clients` « Aucun client » |
| RB3 | ✅ Résolu | `b945841` | `sysadmin.requiredActions` contient `UPDATE_PASSWORD` ; login intercepté |
| RB4 | ✅ Résolu (inc1) | `0e2277a` | tests bUnit (liste/création/reset) + smoke route+DI ; inc2/inc3 à suivre |
| RB5 | ✅ Résolu | `67cbbee` | `<head>` injecte sidebar charbon + `--sidebar-active-accent:#FFE96E` (2 thèmes) |

Finition : realm épuré au **super-admin seul** (cohérent RB2/RB4 — tenants & users créés via la
console) ; revue consolidée RB1→RB5 = propre (P1 OK : socle intact, frontière IdP, tenant-scope,
secrets, injection CSS). Revue par sous-agent (le wrapper codex-review reste à rejouer si besoin).


---

## RB1 — Le super-admin (`sysadmin` / `stratum-admin`) est rattaché à un tenant particulier

- **Vu** : connecté en `sysadmin`, la barre du haut affiche **« Tenant de developpement »** et la
  navigation expose des vues **tenant-scopées** (Documents, Encaissements, Traitements, Table TVA,
  Comptes PA, Paramètres fiscaux…). Capture : onglet Documents du « Tenant de developpement », badge
  `sysadmin` en haut à droite.
- **Attendu** : un super-admin (`stratum-admin`) n'appartient à **aucun tenant** en particulier. Il ne
  doit pas être auto-rattaché à un tenant ni voir les documents/paramètres d'un tenant donné. Il
  devrait atterrir sur un **contexte cross-tenant / administration** (Supervision, Clients, Flotte),
  avec éventuellement un **sélecteur de tenant** pour « entrer » explicitement dans un tenant.
- **Hypothèse technique (à confirmer)** : la résolution de tenant rattache la session via
  `Keycloak:RealmTenantMap` (realm `bucodi` → tenant `default`) **y compris pour un super-admin sans
  `company_id`**. Le `sysadmin` du realm n'a pas d'attribut `company_id` (exempté du cross-check
  fail-closed), mais le mapping realm→tenant le bind quand même au `default`. → la logique de
  résolution devrait NE PAS auto-binder un `stratum-admin` à un tenant.
- **Portée** : comportement **général** (pas spécifique Bucodi) — même effet en dev (`liakont-dev`→
  default) et en appliance (`liakont`→default).
- **Précision (Karl)** : le cœur du problème est **UI** — (1) le **menu de gauche** affiche des entrées
  **tenant-scopées** (Documents, Encaissements, Traitements, Signatures, Paramétrage, Table TVA,
  Comptes PA, Alertes, Agents…) qui n'ont **rien à faire là** pour un super-admin cross-tenant ;
  (2) le **chip tenant en haut** (« Tenant de developpement ») ne devrait **pas s'afficher**. → Fix
  ciblé : masquer les nœuds de nav tenant-scopés + masquer le chip tenant quand le contexte est
  cross-tenant (super-admin sans tenant actif), ne laisser que les entrées cross-tenant (Supervision,
  Clients, Flotte, Audit…).

## RB2 — Une base neuve ne doit pas être auto-seedée (0 tenant, création propre obligatoire)

- **Exigence (Karl)** : au lancement d'une **nouvelle base**, **pas de tenant seeder** ; on doit avoir
  **0 tenant** et être **obligé de créer les tenants proprement** (provisioning / écran Clients).
- **État actuel de la démo** : l'env tourne en `ASPNETCORE_ENVIRONMENT=Development` → `DevTenantSeeder`
  crée automatiquement le tenant `default` (« Tenant de developpement ») + profil fiscal fictif +
  publication SIREN. C'est **ce seeder qui produit le tenant** que voit le sysadmin (lien direct RB1).
- **Note produit** : en mode **Production**, le Host **ne seede pas** (`DevTenantSeeder` est
  Development-only) → une base neuve a déjà 0 tenant, le provisioning passe par `/admin/tenants`
  (écran Clients). Question d'orchestration : faut-il (a) faire tourner la **démo** sans auto-seed
  pour qu'elle reflète le vrai onboarding, et/ou (b) revoir la place du dev-seeder dans le produit ?
- **Lien RB1 ↔ RB2** : avec 0 tenant, plus de `default` auquel binder → corrige en partie le
  symptôme RB1, mais **la logique de résolution super-admin reste le vrai fix de fond** (RB1).

---

## RB3 — Changement de mot de passe obligatoire du super-user à la première connexion

- **Exigence (Karl)** : le **superUser** (`sysadmin`, compte super-admin d'amorçage) doit être
  **forcé de modifier son mot de passe à la première connexion**.
- **État actuel** : dans `deployments/bucodi/keycloak/realm-bucodi.json`, le compte `sysadmin` a un
  mot de passe **non temporaire** (`value: Test@1234`, `temporary: false`) et seulement
  `requiredActions: ["CONFIGURE_TOTP"]` → **aucune** modification de mot de passe forcée.
- **Attendu** : action requise **`UPDATE_PASSWORD`** (et/ou mot de passe **temporaire**) sur le
  super-user à l'amorçage → changement obligatoire au 1er login (en plus de l'enrôlement 2FA).
- **Note** : les utilisateurs provisionnés via la console (`KeycloakTenantUserProvisioner`) reçoivent
  déjà `UPDATE_PASSWORD` + `CONFIGURE_TOTP` ; l'incohérence porte sur le **compte super-admin
  d'amorçage** (seedé) et, plus largement, sur tout compte d'amorçage à mot de passe fixe.

## RB4 — CRUD utilisateurs complet par tenant, depuis l'appli (zéro console Keycloak)

- **Exigence (Karl)** : gérer **entièrement** les utilisateurs du realm Keycloak **depuis l'interface
  Liakont**, **sans jamais ouvrir la console d'admin Keycloak**. **CRUD utilisateur complet, par
  tenant.**
- **Périmètre attendu** : lister / créer / modifier (rôles `lecture`/`operateur`/`parametrage`/
  `superviseur`, profil) / réinitialiser le mot de passe / activer-désactiver / supprimer les
  utilisateurs — **scopé par tenant** (un gestionnaire de tenant ne gère que SES utilisateurs ; le
  super-admin gère tous les tenants, cf. RB1).
- **État actuel (à confirmer)** : la **création** existe (`KeycloakTenantUserProvisioner` + écran
  Clients/provisioning du 1er user). Le reste du CRUD (liste, édition rôles, reset mdp, désactivation,
  suppression) côté UI **reste à vérifier/compléter**. L'API Admin Keycloak est déjà câblée
  (`KeycloakAdminTokenService`), donc le socle technique est là.
- **Note archi** : passer par l'**abstraction IdP (D10)**, pas d'appel Keycloak-spécifique hors de la
  couche d'auth ; respecter le tenant-scoping (blueprint §6, CLAUDE.md n°9).
- **Note sécurité** : le provisioning user s'appuie aujourd'hui sur les creds **admin du realm master**
  (god-mode, dette déjà tracée). Exposer un CRUD complet en UI **amplifie l'enjeu** → la cible
  « service account scopé `liakont-provisioner` (`realm-management:manage-users` sur le seul realm) »
  devient prioritaire.

## RB5 — Le branding par instance ne change pas SIGNIFICATIVEMENT les couleurs (pas opérationnel à l'œil)

- **Vu** : connecté en `sysadmin`, le **menu de gauche reste bleu marine** (défaut Liakont), la **ligne
  sélectionnée** est navy, **aucun jaune Bucodi** visible. « Où sont les couleurs Bucodi ? »
- **Attendu (Karl)** : pouvoir changer **significativement** les couleurs de l'application par
  **paramétrage d'instance, sans dev** — menu de gauche, ligne sélectionnée, fond, etc.
- **Précision (Karl, après échange)** : **thème clair confirmé** (donc le « skip dark » n'est PAS la
  cause ici — c'est le périmètre de tokens trop étroit + la palette charbon≈navy + le jaune planqué).
  Exigence cadrée : un **paramétrage générique, par instance** — *« chaque éditeur a ses couleurs, mais
  une seule version commune entre chaque »*. Donc : **UN seul mécanisme générique** dans le code commun,
  piloté par **config d'instance**, **jamais** un spécifique/fork par éditeur (généricité blueprint
  §2/§6 ; cf. principe [[gtm-connecteur-pas-de-spe-client]] appliqué à l'UI). → item d'orchestration
  PHARE : renforcer BRD01 pour un recoloriage **visible et large** (sidebar, ligne sélectionnée, fond,
  accents), identique pour tous, paramétré par instance.
- **Constats techniques** :
  1. L'override BRD01 **est bien injecté** dans le `<head>` :
     `:root:not([data-theme="dark"]){--color-primary:#1B1D20;--color-primary-600:#1B1D20;--color-primary-700:#1B1D20;--color-primary-container:#FFE96E;}` (vérifié).
  2. **Thème par défaut = préférence OS** (`App.razor` l.13-14 :
     `prefers-color-scheme: dark ? 'dark' : 'light'`, posé sur `<html data-theme>`). En **thème SOMBRE**,
     l'override est **neutralisé** (`:not([data-theme="dark"])`) et `tokens.css` recode la sidebar **en
     dur** (`--sidebar-bg:#0a1628`, `--color-primary:#6b8fd1`) → **0 couleur Bucodi**. → si l'OS de
     l'opérateur est en sombre, le branding est **totalement invisible**.
  3. **Portée trop étroite** : BRD01 ne surcharge que 4 tokens (rampe `--color-primary*` + `-container`).
     La sidebar dérive de `--color-primary-600/700` (OK en clair), mais la **ligne sélectionnée**, le
     **fond de contenu** et les **surfaces** ne sont **pas** pilotés par la marque.
  4. **Choix de palette (de Claude)** : `PrimaryColor=#1B1D20` (charbon) ≈ `#001b44` (navy défaut) →
     changement **quasi imperceptible** ; la couleur **signature jaune `#FFE96E`** est reléguée à
     `--color-primary-container` (hover/secondaire) → **invisible** sur le chrome principal.
- **Conséquence** : l'exigence « branding **significatif** par instance, **sans dev** » n'est **pas
  satisfaite** par BRD01 (couverture de tokens trop étroite **+ thème clair uniquement**).
- **Pistes orchestration** : (a) brander **aussi le thème sombre** et/ou **ancrer un thème par instance**
  (ne pas dépendre de la préférence OS) ; (b) **élargir la couverture** des tokens pilotés par la marque
  (sidebar, ligne sélectionnée/hover, fond, accents) ; (c) garantir une **palette perceptiblement
  distincte** et **surfacer la couleur signature** ; (d) éventuel **éditeur de thème par instance**
  (paramétrage live). NB : le socle `tokens.css` est vendored (P1) → l'extension doit rester côté
  surcharge Host (`BrandingHead`), pas en modifiant le socle.
- **Honnêteté** : ma validation précédente prouvait l'**injection CSS** (vraie), pas le **rendu visuel
  perçu** — qui est, lui, insuffisant. C'est le vrai critère et il n'est pas tenu.

## RB6 — Horodatages affichés en UTC (pas convertis au fuseau local)

- **Vu** : colonne « DERNIER CONTACT » d'un agent = `17/06/2026 18:42` alors qu'il est ~20:46 **locale**
  (CEST = UTC+2). La date n'est pas décalée au fuseau de l'utilisateur.
- **Attendu** : afficher l'heure **locale** de l'opérateur.
- **Cause** : la colonne fait pourtant `.ToLocalTime()` (`Agents.razor`, colonne `LastSeenUtc`), MAIS
  l'appli **Blazor Server tourne dans le conteneur Docker en UTC** → `ToLocalTime()` côté serveur ne
  convertit rien (le serveur EST en UTC). **Portée** : TOUS les horodatages rendus ainsi (dernier
  contact, dates de documents, créations…). **Pré-existant**, sans rapport avec AGT03.
- **Fix retenu (Karl) = (b) PROPRE / PRODUIT** : convertir au fuseau du **navigateur** (JS interop —
  `Intl.DateTimeFormat` / offset, résolu une fois par circuit), correct en multi-tenant/multi-région.
  Écartées : (a) `TZ=Europe/Paris` sur le conteneur = mono-zone seulement ; (c) suffixe « UTC » = honnête
  mais pas local. → généraliser via un **helper commun** de formatage date/heure côté UI (pas seulement
  la page Agents), socle vendored non modifié.
- **✅ Infra + P0 livrés** : `IBrowserTimeZone` (scopé/circuit) résout le fuseau du navigateur via JS interop
  Liakont (hors socle), `<LiakontDate>` formate au fuseau résolu (repli UTC explicite avant résolution),
  sonde `<BrowserTimeProbe>` dans le shell. 9 sites P0 migrés (le bug visible : Agents, Documents, Supervision…)
  + 20 tests verts. **DateOnly (dates calendaires) laissées en l'état** (sans fuseau).
- **✅ P1 livrés** (session préc.) : Flotte (LastSeen/backup), Signatures (OccurredAt).
- **✅ P2 livrés (nuit 17→18/06)** : 4 modules d'admin du SOCLE migrés — Job (176e1f0), Audit (442b418),
  Identity (4957aee), Notification (57bc1bf) = 25 pages. **RÈGLE figée** : ÉVÉNEMENTS → fuseau navigateur ;
  PRÉVISIONS serveur (cron) → UTC EXPLICITE ; dates de VALIDITÉ (DateOnly / échéances / durées) → laissées.
  Infra de test partagée (`AdminPageTestServices` + `FakeGridPreferenceService`), 1 test bUnit/page (règle 19),
  provenance §4.32–4.35. verify-fast + build Release (StyleCop) + codex-review verts par module.
- **À VÉRIFIER par Karl en recette** : « Dernier contact » de l'agent doit afficher l'heure LOCALE (et non 18:42 UTC).

## RB7 — L'assistant d'installation de l'agent ne démarre pas le service en fin d'installation

- **Vu** : après « Installer l'agent » (assistant GUI), le service `LiakontAgent$<instance>` est
  **enregistré mais à l'arrêt** ; il faut le démarrer à la main (`services.msc` / `sc start`).
- **Attendu** : l'assistant **démarre le service lui-même** en fin d'installation (un install réussi = un
  agent qui tourne).
- **Cause** : `AgentProcessDeployer.TryRunServiceInstaller` lance `Liakont.Agent.exe install` (enregistre
  le service + démarrage auto au prochain boot) mais ne fait **pas** le `sc start` immédiat — le script
  `deployments/demo-local/install-services.ps1` le faisait, lui, en deux temps (`install` puis `sc start`).
- **Fix** : après l'install du service + `check-config` OK, **démarrer le service** et refléter l'état
  *Running* dans l'écran Résumé du wizard. Périmètre : `AgentProcessDeployer` (OPS08b).
- **✅ Résolu (nuit 17→18/06)** : `AgentProcessDeployer.TryStartService` démarre le service après l'install
  et attend le passage *Running* (30 s) ; un échec de démarrage NE défait PAS l'install (service enregistré
  en auto-démarrage) et remonte une ligne `[!]` avec l'action corrective. verify-fast + run-tests verts ;
  codex-review propre. **COUVERTURE = recette manuelle** (comme tout le déployeur de prod : `ServiceController`
  pilote le SCM réel, non mockable sans abstraction — pas de sur-architecture au stade build). → **à VÉRIFIER
  par Karl en recette** : après l'assistant, le service `LiakontAgent$<instance>` doit être *En cours
  d'exécution* sans action manuelle.

## RB8 — Planification d'extraction NON appliquée (cadence figée à 1 min ; champ « Planification » inerte)

- **Constat (vérifié code)** : le service extrait **toutes les minutes** quel que soit le `schedule`.
  `AgentHost.Create` câble `AgentBackgroundRunner(runCycle, TimeSpan.FromMinutes(1))` (`AgentHost.cs:64`)
  → boucle à **intervalle fixe**. `AgentRunCycle.Run` extrait `[filigrane, maintenant[` **sans consulter
  la planif** ; `AgentRunComposition.Build` ne passe **pas** le `schedule` au cycle. (Les 12 docs de la
  démo sont arrivés dans la minute, pas à l'heure planifiée.)
- **La planif existe mais n'est pas branchée** : `extraction.schedule` (HH:mm local) + cron poussé par la
  plateforme sont **résolus** (`EffectiveExtractionPlan`) et **remontés** au heartbeat, mais **jamais
  appliqués** pour cadencer les runs. Le code l'assume : pilotage planif « porté par l'hôte et AGT03 » →
  non câblé en AGT02.
- **Conséquences** : (1) le champ **« Planification »** de l'assistant d'install est une **false
  affordance** (stocké, sans effet) ; (2) cadence **non réglable** (1 min fixe) — potentiellement trop
  agressif sur une vraie base ERP en production.
- **Fix (AGT03)** : faire **consulter `EffectiveExtractionPlan`** par le runner (calcul du prochain run :
  HH:mm local ou cron plateforme) pour gater les runs, au lieu de l'intervalle fixe. En attendant :
  masquer/désactiver le champ « Planification » de l'assistant (ou l'appliquer), pour ne pas laisser
  croire qu'il pilote la cadence.

## RB9 — [P1] AGT03 casse l'idempotence anti-doublon (le `payload_hash` dépend du profil tenant)

- **Constat (vérifié base)** : `A-2026-0014` (même document source) **en double**, deux `payload_hash`
  différents (`1b303340…` à 18:43, `feb54374…` à 18:49). Entre les deux, le **paramétrage fiscal**
  (nature d'opération) de SEM Keroman a été renseigné.
- **Cause (défaut de conception AGT03)** : `PivotEmitterEnricher` injecte l'émetteur **et**
  `operationCategory` dans le pivot **AVANT** `CanonicalJson.Serialize` + `payloadHash`
  (`IngestDocumentBatchHandler`, choix « enrichir avant le hash » pour l'intégrité du staging). Donc le
  `payload_hash` — clé anti-doublon F06 avec `source_reference` — **dépend désormais du profil/fiscal du
  tenant**, plus seulement du document source. Un changement de config tenant entre deux extractions de la
  MÊME source → hash différent → l'anti-doublon le voit comme une **altération** (`SourceAlterationDetectedV1`)
  → **nouveau document** (doublon spurious). L'altération F06 (censée détecter une modif de la **source**)
  est polluée par des données injectées plateforme.
- **Gravité** : F06 (anti-doublon / détection d'altération) est un **invariant fiscal (P1)**. Atténué à
  l'ENVOI (idempotence PA sur le n° BT-1 → pas de double transmission), mais le **dédup document est cassé**
  et l'altération devient un **faux positif** à chaque changement de paramétrage.
- **Fix** : le hash anti-doublon doit porter sur le pivot **SOURCE** (ce que l'agent a extrait), jamais sur
  l'enrichi. (a) hasher le pivot **brut agent** pour l'anti-doublon + stager l'**enrichi** pour l'émission
  (découpler *identité source* vs *contenu émis*) ; OU (b) enrichir au **read-time** (CHECK/SEND) au lieu de
  l'ingestion-avant-hash. → **revient sur la décision « enrichir avant le hash »** du plan AGT03 (à
  ré-trancher : intégrité staging vs idempotence — l'idempotence prime).

## RB10 — [PRIORITAIRE] SuperPDP absent de la liste « Type de plug-in PA » (plug-in jamais câblé au Host)

- **Vu (Karl)** : impossible de paramétrer un compte SuperPDP en PA — la liste déroulante n'affiche que
  **Fake** et **Generique**.
- **Cause racine (vérifiée code)** : le plug-in SuperPDP **existe** (`src/PaClients/Liakont.PaClients.SuperPdp/`,
  codé + testé, envoi sandbox réel validé) mais **n'est jamais référencé/enregistré par le Host**. Le Host
  ne câble que `AddFakePaClient()` (`PaClientBootstrap.cs:52`) et `AddGeneriquePaClient()` (via
  `AddGeneriquePaDelivery`, `AppBootstrap.cs:332`). Aucun `AddSuperPdpPaClient()`. La liste est peuplée
  depuis le **registre** des fabriques enregistrées → SuperPDP n'y est pas. Écrit explicitement dans le
  code : `PaClientBootstrap.cs:32-37` (« les vraies PA, B2Brouter, Super PDP, s'ajouteront ici quand leur
  câblage de production, secrets chiffrés par tenant, sera défini »). Confirmé : envoi réel d'avant validé
  via le **harnais de test**, pas via la console produit.
- **Décalage de creds** : le formulaire « compte PA » ne capture qu'une **Clé API** unique ; SuperPDP
  s'authentifie en **OAuth2** (`SuperPdpAccountConfig` = `accountId` + `client_id` + `client_secret` +
  environnement). 3 valeurs requises, 2 champs disponibles.
- **DÉCISION KARL (2026-06-17) = OPTION 1** : champs d'auth **génériques par mode** — ajouter au modèle de
  compte PA des champs OAuth2 (`client_id` + `client_secret`, chiffrés par tenant) à côté de la clé API ;
  le plug-in **déclare son mode d'auth** (capacité) ; le form affiche les bons champs selon le type. Aucun
  spécifique `if (pa is SuperPdp)` (CLAUDE.md n°8/16). PRIORITAIRE — **item suivant à corriger après RB9**.
- **Fix (portée)** : (a) modèle de compte PA + migration DB (colonnes OAuth chiffrées par tenant) ;
  (b) UI form conditionnelle au mode d'auth déclaré ; (c) `ISuperPdpAccountResolver` côté Host (déchiffre
  depuis le coffre TenantSettings) ; (d) Host `ProjectReference` SuperPdp + `AddSuperPdpPaClient()` au
  composition root ; (e) tests. NB : SuperPDP ne marche **qu'en Sandbox** aujourd'hui (`BaseUrl` lève en
  Production, F14 §12 O1).

## RB11 — [P1, ✅ RÉSOLU 18/06 — 926c05a] Horodatages socle lus en `DateTimeOffset` → `InvalidCastException`

- **Vu** : pages d'admin socle (supervision/alertes, `/admin/jobs`, `/admin/notifications/*`) qui plantent ;
  bandeau supervision en boucle d'erreur dans les logs.
- **Cause** : les modules socle Job/Notification/Identity lisaient les colonnes `timestamptz` par cast direct
  `(DateTimeOffset)row.x` ; Npgsql renvoie un `DateTime` (UTC) → `InvalidCastException`. Latent depuis le
  vendoring SOL01, masqué par les tests bUnit (données mockées, jamais de vrai Postgres).
- **Fix** : helper `Stratum.Common.Infrastructure.Database.DbTimestamp` + remplacement des ~80 casts (Audit
  déjà sain). Provenance §4.36. `DbTimestampTests` + build Release 0/0 + run-tests verts.

## RB12 — [P1, ✅ RÉSOLU 18/06 — a7b2ba7] Deadlock à la résolution d'un compte SuperPdp (pages figées)

- **Vu** : dès qu'un compte SuperPdp **actif** existait, **Documents / Comptes PA / Vue d'ensemble** ne se
  rendaient plus (URL + menu changent, mais contenu figé sur la page précédente), heartbeat agent bloqué,
  save du compte PA en spinner infini. **Pas d'exception**, WebSocket `/_blazor` en 101.
- **Cause** : `SuperPdpAccountResolver.Resolve` = `ResolveAsync(...).GetAwaiter().GetResult()` (sync-over-async).
  Appelé au **RENDU UI** (`BuildPaAccountSettings` décrit le compte) sous le `SynchronizationContext` du circuit
  Blazor Server, le `DisposeAsync` du scope tenant (`await using`) tentait de reprendre sur le thread du circuit,
  bloqué par le `.GetResult()` → **deadlock**.
- **Fix** : `Task.Run(() => ResolveAsync(account)).GetAwaiter().GetResult()` (offload hors `SynchronizationContext`)
  + garde anti-régression (test reproduisant le deadlock sous un SC mono-thread, 8/8).

## RB13 — [OUVERT] Compte PA désactivé INVISIBLE + recréation du même type bloquée (impasse)

- **Vu** : créer un compte SuperPdp, le **désactiver**, puis vouloir **recréer** un compte du même type →
  blocage « Un compte plateforme agréée existe déjà pour ce plug-in et cet environnement » (`ComptesPa.razor:301`),
  **MAIS** le compte désactivé n'apparaît **nulle part** dans l'UI → impasse (ni gérable, ni réactivable, ni recréable).
- **Cause** : la liste des comptes PA n'expose pas les comptes **inactifs**, alors que la contrainte d'unicité
  (plugin_type + environnement) les **compte**.
- **Fix (à décider)** : (a) lister les comptes inactifs avec réactiver/supprimer ; OU (b) recréation = réactivation
  implicite ; OU (c) unicité portant sur les **actifs** seulement.
- **Contournement recette (18/06)** : compte désactivé supprimé en base (`DELETE`) pour débloquer.

## RB14 — [OUVERT, cosmétique] Bandeau de reconnexion Blazor « Rejoining the server… » en anglais

- **Vu** : après un redémarrage du Host, l'onglet affiche « Rejoining the server… » (bandeau Blazor non localisé).
- **Fix** : franciser le `components-reconnect-modal` (côté Host, `App.razor`).

## RB15 — [✅ RÉSOLU 18/06 — b5024ce] SuperPDP affichait « Facturation B2B : Non disponible » à tort

- **Vu** : capacité `SupportsB2bInvoicing = false` (« phase 2 ») alors que l'envoi B2B est **vérifié en sandbox**
  (envoi réel facture 72272) et que `SendDocumentAsync` ne la garde pas → affichage trompeur, démo bloquée.
- **Fix** : `SupportsB2bInvoicing = true` (directive de recette Karl 18/06) + test aligné (10/10). Les autres
  capacités non confirmées restent `false` (principe « moins-disant »).

---

# Session nuit 18→19/06/2026 — Envoi B2B réel vers SuperPDP de bout en bout

> Objectif : faire partir une vraie facture vers la sandbox SuperPDP via la console. **Résultat : ✅ 6
> factures B2B ÉMISES** (Issued, IDs SuperPDP **#79196 → #79201**) — chaîne complète extraction → mapping
> TVA → identité émetteur → conversion CII → POST /invoices → émission asynchrone confirmée.
>
> ⚠️ **Tous les correctifs ci-dessous sont en WORKING TREE, NON COMMITTÉS** (Karl : review + commit + tests
> au calme ensuite). Plusieurs sont des **scories d'agents précédents** (sur-restrictions / oublis), pas des
> bugs « naturels ». Chaque correctif révélait le suivant (aucun envoi B2B n'avait jamais traversé toute la
> chaîne avant cette nuit).
>
> Paramétrage de recette posé : profil tenant = **Burger Queen / SIREN `000000002`** (la company du compte
> sandbox de Karl ; `315143296` qu'il voyait = ID de COMPTE / préfixe annuaire, PAS un SIREN) ; acheteur de
> test adressable = **Tricatel `000000001`**. Script de seed rejouable livré :
> `deployments/demo-local/seed-factures-b2b.ps1`.

## RB16 — [OUVERT] L'agent ne communique qu'après un redémarrage MANUEL du service (heartbeat muet)

- **Vu** : après installation + auto-démarrage du service, **un seul heartbeat** reçu (« dernier contact »
  figé) ; l'agent ne pousse plus rien. Un `Restart-Service` manuel débloque tout (heartbeat + extraction).
- **Cause (à investiguer)** : quelque chose dans le démarrage auto post-install diffère du démarrage manuel
  (timing de lecture de config / DPAPI / réseau au boot ?). Lien possible avec RB7 (démarrage auto du service).
- **Fix** : à diagnostiquer — l'agent doit communiquer dès le 1er démarrage auto, sans intervention.

## RB17 — [✅ corrigé working tree] « SIREN non publié / tax_report_setting inactif » bloquait tout envoi

- **Vu** : tout envoi SuperPDP refusé en amont — « SIREN non publié / paramétrage de transmission inactif ».
- **Cause** : `SuperPdpClient.GetTaxReportSettingAsync`/`EnsureTaxReportSettingAsync` = **bouchons no-op**
  (PAS02, endpoint « à confirmer »). Le pré-check SEND lisait donc un réglage TOUJOURS vide → fail-closed.
  Or chez SuperPDP, « publier le SIREN » = **vérification KYC de la company** (faite dans leur espace), pas
  un endpoint produit — et la company de Karl est déjà vérifiée (envois réels passés).
- **Fix** : câbler la **lecture réelle de l'état** via `GET /v1.beta/companies/me` (endpoint confirmé) — si la
  company existe avec un SIREN ⇒ transmission active. `EnsureTaxReportSettingAsync` = simple constat (no-op si
  vérifiée, message actionnable sinon). (O2 partiellement levé.)

## RB18 — [✅ corrigé working tree + paramétrage] SIREN émetteur faux + Luhn refusant les SIREN sandbox

- **Vu** : profil tenant configuré avec `315143296` ; envoi refusé (vendeur ≠ company de session). Et les SIREN
  sandbox (`00000000X`) échouaient le contrôle de **clé de Luhn**.
- **Cause** : `315143296` est l'**identifiant de compte** SuperPDP (préfixe des adresses annuaire
  `315143296_120xx`), pas le SIREN ; la vraie company = Burger Queen **`000000002`**. Et les SIREN fictifs des
  bacs à sable ne satisfont jamais la clé de Luhn.
- **Fix** : profil aligné sur `000000002` (paramétrage). **TVA émetteur dérivée du SIREN** (`FR`+clé, ex.
  `FR18000000002`) dans `PivotEmitterEnricher` — le pipeline ne la remplissait pas (`vatNumber: null`), or
  EN 16931 l'exige (BR-S-02). **Luhn assoupli pour les SIREN de test** (préfixe `00000`) dans les deux
  `SirenValidator` (Validation + TenantSettings) — décision Karl.

## RB19 — [✅ corrigé working tree — MAJEUR] Le B2B était BLOQUÉ en V1 (garde-fou « tout est B2C »)

- **Vu** : une facture à acheteur professionnel était **bloquée** au CHECK — « facture électronique B2B
  requise, **non gérée automatiquement en V1** — traitez manuellement ou confirmez qu'il s'agit d'un
  particulier ». Aucune vente B2B ne pouvait partir.
- **Cause (SCORIE d'agent)** : `BuyerLooksProfessionalRule` (VAL05) bloquait tout acheteur « pro » au motif que
  le B2B n'était « pas géré en V1 » — une **réduction erronée du périmètre à « criée = B2C »**. Or l'e-invoicing
  B2B est le **cœur de la réforme**. Réaction de Karl : *« comment c'est possible un truc pareil ? évidemment
  que ça doit gérer le B2B en V1 ».*
- **Fix** : un acheteur **identifié par un SIREN** = vente B2B **émettable** = flux nominal (plus de blocage) ;
  le garde-fou ne couvre plus que le « pseudo-pro » SANS SIREN (ni adressable, ni sûrement particulier). Tests
  11/11. **`Liakont DOIT gérer le B2B en V1`** (à graver).

## RB20 — [✅ corrigé working tree] Mapping TVA OUBLIÉ au SEND (catégorie absente du pivot transmis)

- **Vu** : `Ventilation de TVA sans catégorie UNCL5305` (exception au plug-in) → erreur d'envoi. Révélé
  seulement maintenant (les B2C étaient rejetés AVANT d'atteindre le constructeur de payload).
- **Cause** : la branche `emitter-filled-by-platform` a déplacé l'enrichissement au **read-time** mais n'a
  ré-appliqué que **l'émetteur** — la **catégorie TVA** (posée au CHECK) n'était PAS reportée sur le pivot
  transmis (le staging = pivot SOURCE, régimes bruts). « Ça marchait avant » = quand le pivot stagé portait
  déjà la catégorie.
- **Fix** : reposer le mapping TVA au SEND (`SendTenantJob.ReadStagedPivotAsync`), **symétrique à l'émetteur**,
  via le même moteur qu'au CHECK (`CheckTvaMapping`). Nouveau statut `TvaUnresolved` (HOLD différé si la table
  a changé entre CHECK et SEND).

## RB21 — [⚠️ CONTOURNEMENT TEMPORAIRE + DETTE] Conformité FR : facture non soldée + mentions légales absentes

- **Vu** : rejet du **converter EN 16931/FR** de SuperPDP (la facture l'atteint enfin) :
  - **[BR-CO-25]** montant dû positif sans échéance (BT-9) ni conditions de paiement (BT-20) ;
  - **[BR-FR-05]** mentions légales FR obligatoires absentes des notes (PMT/frais de recouvrement,
    PMD/pénalités de retard, AAB/escompte).
- **Cause** : (a) l'adaptateur de démo ne porte ni acompte ni échéance → montant dû positif ; (b) le produit ne
  **génère pas** les mentions légales FR. **Ce ne sont PAS des scories — ce sont de vraies exigences de
  conformité française** (SuperPDP fait correctement son travail).
- **Contournement (TEMPORAIRE, marqué)** : dans `SuperPdpPayloadBuilder`, une facture **sans acompte** est
  traitée **SOLDÉE** (comptant criée) → montant dû nul ⇒ lève BR-CO-25 **et** BR-FR-05 (validé : le test
  sandbox soldé passe). **À REMPLACER** par l'acompte/échéance RÉELS portés par l'adaptateur source.
- **DETTE produit** : générer les **mentions légales FR** (BR-FR-05) proprement — vraie fonctionnalité.

## RB22 — [✅ corrigé working tree] L'émission ASYNCHRONE de SuperPDP affichée en « erreur technique »

- **Vu** : facture **téléversée chez SuperPDP** (POST `/invoices` → **200**) mais Liakont affichait « ⚠️ Erreur
  technique » (faux échec).
- **Cause** : SuperPDP est **asynchrone** (200 = `api:uploaded` téléversée ; l'émission `fr:201` suit en ~2 s).
  Le pipeline (`HandleSendResultAsync`) n'avait **pas de cas pour `Sending`** → tombait dans `default` →
  `MarkTechnicalError`. Jamais révélé car aucun POST B2B n'avait abouti avant.
- **Fix** : `case PaSendState.Sending → Deferred` (le document reste « en cours d'envoi », ni succès ni échec) ;
  le raccrochage (relecture d'état / anti-doublon par `external_id`) le finalise **Issued** au cycle suivant.
  Vérifié : `Sending → Issued` proprement (IDs #79199-79201).

## RB23 — [OUVERT, dette] Des références internes `(CLAUDE.md n°X)` fuitent dans les messages d'erreur PRODUIT

- **Vu (Karl)** : un message d'erreur du converter citait `(CLAUDE.md n°2)`. *« CLAUDE.md dans un message
  d'erreur ?! »* — `CLAUDE.md` est le fichier d'instructions internes de l'agent, il n'a rien à faire dans un
  message produit.
- **Fix** : nettoyer le code (commentaires de messages opérateur / exceptions) — remplacer les `(CLAUDE.md n°X)`
  par des références de spec réelles (F03, EN 16931, BOI…) ou rien. Grep `CLAUDE.md` dans le code produit.

## RB24 — [OUVERT] Préférences perdues au changement de navigateur (application via localStorage, pas la base au login)

- **Vu (Karl)** : les préférences (thème, densité, taille de page) sont **stockées/appliquées côté navigateur**
  → en **changeant de navigateur, on les perd**. Attendu : préférences **par utilisateur**, qui le suivent quel
  que soit le poste/navigateur.
- **Constat (vérifié code)** : une persistance **EN BASE existe pourtant** — `IUserPreferencesService` /
  `PostgresUserPreferencesService` + table `identity.user_preferences` (migration V012) pour thème/densité/langue,
  et `PersistedLanguageRequestCultureProvider` pour la langue. **MAIS** :
  1. L'application **LIVE** du thème/densité repose sur **localStorage** (`stratumUI.setDensity/getDensity`,
     `setTheme/getTheme`). La valeur base n'est **ré-appliquée vers le JS que par le PANNEAU** de préférences
     (`UserPreferencesPanel.OnAfterRender`), **pas globalement au login**. Sur un navigateur neuf → localStorage
     vide → l'appli affiche le **défaut** (OS/JS), la préférence base est ignorée tant que le panneau n'est pas ouvert.
  2. La **taille de page des grilles** reste **volontairement en localStorage** (commentaire « out of scope GUX06 »).
- **Fix** : **hydrater** la préférence base → couche client **dès le login / le rendu du shell** (thème + densité +
  pagination), pour que la base soit la source appliquée partout, pas seulement dans le panneau. Décider du sort du
  grid page size (le passer en base aussi, cohérent avec « suit l'utilisateur »).

## RB25 — [OUVERT, simple] Densité par défaut = Compact ; doit être Standard

- **Vu (Karl)** : la densité par défaut affichée est **Compact** (sélectionné). Exigence : **défaut = Standard**.
- **Constat** : le **modèle base** `UserPreferences.Density` a déjà **`"standard"`** par défaut, et le panneau
  initialise `_density = DensityStandard`. Le **Compact** vient donc de la **couche JS** : quand aucune préférence
  n'est posée, `stratumUI.getDensity()` (socle `stratum-ui.js`) renvoie/applique **compact** → défaut EFFECTIF = compact.
- **Fix** : aligner le **défaut de la couche client** sur **`standard`** (socle `stratum-ui.js` — attention provenance
  socle vendored ; sinon forcer l'hydratation « standard » au login si pas de pref). Trivial, mais à faire au bon
  endroit pour rester cohérent avec RB24.

## RB26 — [OUVERT] Page Documents : pas de refresh auto après traitement + bandeau « différés (staging absent) » trompeur

- **Vu (Karl)** : après « Lancer un traitement », (1) la **liste ne se rafraîchit pas** automatiquement (il
  faut **actualiser la page** pour voir les nouveaux états) ; (2) le **bandeau ROUGE** affiche « *Le traitement
  d'envoi du tenant est terminé : aucun document émis. SEND : 0 émis, 0 en échec, **6 différés (staging absent)**,
  0 ignorés.* » — **trompeur** : tout s'est bien passé, les 6 documents sont **téléversés à la PA** (statut **« En
  cours »**) et passeront **« Émis »** au **prochain traitement**. Confirmé par les captures suivantes : après
  actualisation → 6 « En cours » ; au traitement suivant → **retours PA corrects** (19 « Émis », 2 « Rejeté »).
- **Cause** :
  1. **UI** : la grille Documents ne **re-fetch pas** après le déclenchement d'un traitement (`Tout envoyer` /
     `Lancer un traitement`) — l'état affiché reste celui d'avant.
  2. **Message inadapté (effet de bord de RB22)** : le correctif « état `Sending` asynchrone » classe les
     documents **téléversés** en `SendOutcome.Deferred` ; or le `tally.Describe()` annote tout différé
     « **(staging absent)** » → libellé **faux** pour un document parti avec succès, et le bandeau **« aucun
     document émis » en ROUGE** donne une fausse impression d'**échec** alors que l'envoi a réussi (émission
     asynchrone en cours).
- **Fix** :
  1. **Rafraîchir la liste** après un traitement (re-fetch + maj des compteurs d'onglets).
  2. **Distinguer « téléversé / en cours d'émission » de « différé (staging absent) »** : un `SendOutcome`
     dédié (ou un compteur séparé) + message **non alarmant** (« *N document(s) téléversé(s) à la PA — émission
     en cours, confirmée au prochain traitement* ») et **bandeau info (pas rouge)** quand des documents sont
     partis correctement. Réserver le rouge aux vrais échecs (rejet / erreur technique).
- **À confirmer** : les **2 « Rejeté »** du 2ᵉ traitement (BQ-2026-118/120) — vrai rejet PA ou effet de
  re-soumission/anti-doublon ? À investiguer (le **retour PA fonctionne**, c'est la cause des 2 rejets qui reste à qualifier).

## RB27 — [OUVERT, P2 opérateur] Le motif EXACT du rejet PA n'est affiché nulle part dans l'UI

- **Vu (Karl)** : un document **« Rejeté »** affiche l'état, mais **pas le retour exact de la Plateforme Agréée**
  → l'opérateur **ne sait pas ce qui a posé problème** ni quoi corriger. (Cette nuit, le motif `[BR-CO-25]` /
  `[BR-FR-05]` n'a pu être obtenu qu'en **SQL** : `documents.document_events.pa_response_snapshot`.)
- **Cause** : le message/les erreurs de la PA sont bien **persistés** (piste d'audit : `pa_response_snapshot`,
  `errors[]` du `DocumentRejectionSnapshots`) mais **non surfacés** dans le détail du document. L'UI montre
  l'**état** (`DocumentStateDisplay`) et la **timeline d'events** (`DocumentEventDisplay`), pas le **message PA
  détaillé**. → exigence **CLAUDE.md n°12** (message opérateur FR + **action corrective**) **non tenue** pour les rejets PA.
- **Fix** : sur un document **Rejeté / en erreur technique**, afficher dans le détail le **message EXACT de la PA**
  (texte + code, ex. `[BR-CO-25]…`), l'**horodatage**, et une **action corrective** — lu depuis
  `pa_response_snapshot` / `errors[]`. C'est *le* livrable qui rend les rejets exploitables sans accès base.

## RB28 — [OUVERT] Le bouton « Actualiser » des listes ne fonctionne pas correctement

- **Vu (Karl)** : le bouton **actualiser** (icône refresh circulaire) de la barre d'outils des grilles **ne
  rafraîchit pas correctement** la liste (lien direct avec RB26 : même en forçant, l'état n'est pas mis à jour ;
  seul un **rechargement complet de la page** fonctionne).
- **Cause (à confirmer)** : le bouton refresh de la grille (`StratumDataGrid` / barre d'outils) ne déclenche
  **pas un vrai re-fetch** serveur des données + **re-comptage** des onglets (Tous / Prêt / En cours / Émis /
  Rejeté), ou ne redessine pas après le fetch.
- **Fix** : le bouton « Actualiser » doit **re-interroger** la source (mêmes filtres/période) **et** recalculer
  les compteurs d'onglets, puis re-rendre — sans rechargement de page.

## RB29 — [AUDIT + MISE AU PROPRE] verify-fast ROUGE (faux-verts) + 2 hacks retirés → dette inscrite (lot RBF)

Audit adversarial du working tree (workflow 19 agents) à la demande de Karl (« a-t-on du code sale ? »).

**⚠️ Fait dominant : `verify-fast` était ROUGE.** Les marqueurs « ✅ corrigé working tree » des RB17/RB19/RB20/
RB21/RB22 ci-dessus étaient des **FAUX-VERTS** : 11 tests cassaient (2 SuperPDP + 9 SendTenantJob — le SEND
appelle désormais `ITvaMappingService` non enregistré dans le harnais, et les nouvelles branches HOLD/Sending
n'avaient aucun test). **Aucune déclaration « corrigé » n'était valide.** → réparé/testé par **RBF03**.

**Hacks RETIRÉS en interactif (cette nuit) :**
- **A1 — « facture soldée d'office »** (`SuperPdpPayloadBuilder`) : forçait `PaidAmount = TotalGross` / montant
  dû = 0 → donnée fiscale FABRIQUÉE (paiement inexistant déclaré à l'administration), contournait BR-CO-25.
  **Retiré** (retour à `PaidAmount = PrepaidAmount`). Le converter rejette à nouveau une facture non soldée
  sans échéance — comportement correct. Vrai fix = **RBF01** (acompte/échéance à la source + mentions FR).
- **A2 — bypass Luhn sur le SIREN ACHETEUR** (`Validation.SirenValidator.IsValid`, préfixe `00000`) : affaiblissait
  une garde Blocking sur une donnée EXTRAITE (non demandé — Karl n'avait parlé que de SON SIREN émetteur).
  **Retiré** (acheteur de nouveau Luhn-strict). Tests réétiquetés (`315143296` n'est pas un SIREN mais l'ID de
  compte SuperPDP → `000000002`). Mise au propre complète = **RBF02**.

**Reste assoupli (décision Karl, à mettre au propre) :** le SIREN ÉMETTEUR paramétré est accepté sans Luhn
(`SupplierIdentityRule` → `IsWellFormed` ; `TenantSettings.SirenValidator` format-only). Conservé pour la recette,
mais non sourcé (F04 §4.1 n'autorise que La Poste) → **RBF02** doit le porter par PARAMÉTRAGE/contexte sandbox
(hors validateur produit) + amender F04/F12-A + INV-*.

**➡️ TOUTE la dette est inscrite dans le backlog des agents autonomes : lot `RBF` (Recette Bucodi Fixes),
`orchestration/items/RBF.yaml` + `manifest.yaml` v30** (segment `recette-bucodi`, gate humaine
`GATE_RECETTE_BUCODI`). 9 items RBF01→RBF09 couvrant : conformité facture FR (RBF01), Luhn propre (RBF02),
tests/verify-fast vert (RBF03), amendement spec B2B/B2C (RBF04), traçabilité SuperPDP + refs CLAUDE.md (RBF05),
motif de rejet PA dans l'UI (RBF06 ← RB27), refresh listes (RBF07 ← RB26/RB28), préférences en base + densité
défaut (RBF08 ← RB24/RB25), heartbeat agent au 1er démarrage (RBF09 ← RB16).
⚠️ Le lot RBF est à **seeder dans `$ORCH_REPO/state.yaml`** (pending) après merge, comme les autres lots planifiés.

<!-- Prochaines observations dictées : RB30, … -->
