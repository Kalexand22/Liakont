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

<!-- Prochaines observations dictées : RB6, … -->
