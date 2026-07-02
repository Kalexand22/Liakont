# Identité Liakont — permissions, rôles et matrice

Ce document décrit le modèle d'autorisation propre à Liakont : les **permissions**
applicatives, les **rôles** standard, et la **matrice** qui relie les deux.

## 1. Permissions (`LiakontPermissions`)

Les permissions sont des constantes déclarées dans
`src/Host/Liakont.Host/Security/LiakontPermissions.cs`. Elles sont découvertes au
démarrage par `ReflectionPermissionCatalog` (module Identity), qui scanne les assemblies
`Stratum.*` **et** `Liakont.*` à la recherche des classes statiques `*Permissions` exposant
des champs `public const string`.

| Permission | Constante | Capacité |
|---|---|---|
| `liakont.read` | `LiakontPermissions.Read` | Consultation : documents, transmissions, journaux (aucune action mutante). |
| `liakont.actions` | `LiakontPermissions.Actions` | Actions opérateur : déblocage, relance, ré-émission, actions correctives. |
| `liakont.settings` | `LiakontPermissions.Settings` | Paramétrage fiscal du tenant : table TVA, mappings, comptes Plateforme Agréée, seuils. |
| `liakont.supervision` | `LiakontPermissions.Supervision` | Supervision : vues cross-tenant en lecture seule (module Supervision) **et administration d'instance** (OPS03 : écran Clients — création de tenants, suspension/réactivation ; les actions dispatchent in-process dans le scope du tenant cible, la garde de page est l'unique contrôle de ce chemin, parité WEB09). |
| `liakont.instance.settings` | `LiakontPermissions.InstanceSettings` | Paramétrage **MUTANT** d'instance (ADR-0039), hors tenant : configuration d'envoi d'emails de l'instance (SMTP basic / Gmail / O365 OAuth2, secrets chiffrés). Distincte de `liakont.supervision` (lecture seule) : une écriture ne s'accroche pas à une permission « lecture seule ». |
| `liakont.fleet` | `LiakontPermissions.Fleet` | Méta-supervision de flotte (OPS04) : dashboard cross-**instance** d'IT Innovations (état des instances, versions, alertes). Niveau au-dessus de `liakont.supervision`. |

## 2. Rôles standard (realm Keycloak)

Les rôles vivent dans le realm Keycloak `liakont-dev`
(`deploy/docker/keycloak/realm-export.json`) en tant que **rôles de realm**. Le rôle par
défaut attribué à tout nouvel utilisateur est `lecture`.

| Rôle | Description |
|---|---|
| `lecture` | Consultation seule. |
| `operateur` | Consultation + actions opérateur. |
| `parametrage` | Consultation + actions + paramétrage fiscal du tenant. |
| `superviseur` | Toutes les capacités, dont la supervision cross-tenant. |
| `exploitant` | **IT Innovations** (exploitant) : méta-supervision de flotte cross-**instance** (OPS04). N'accorde **aucune** permission éditeur — uniquement `liakont.fleet`. |

## 3. Matrice permission → rôle

| Permission \ Rôle | `lecture` | `operateur` | `parametrage` | `superviseur` |
|---|:---:|:---:|:---:|:---:|
| `liakont.read` | ✔ | ✔ | ✔ | ✔ |
| `liakont.actions` |  | ✔ | ✔ | ✔ |
| `liakont.settings` |  |  | ✔ | ✔ |
| `liakont.supervision` |  |  |  | ✔ |
| `liakont.instance.settings` |  |  |  | ✔ |

Résumé : `lecture` → read ; `operateur` → read + actions ; `parametrage` → read + actions
+ settings ; `superviseur` → read + actions + settings + supervision + instance.settings
(l'opérateur d'instance : la supervision cross-tenant en lecture ET le paramétrage d'instance
en écriture, ADR-0039).

> La supervision cross-tenant reste en **lecture seule** (CLAUDE.md n.9 : seul le module
> Supervision a des vues cross-tenant ; toute autre requête métier est tenant-scopée).
> L'écran **Clients** (OPS03) n'y déroge pas : sa liste compose le REGISTRE système
> (`outbox.tenants`, hors modules métier) avec le profil de chaque tenant lu DANS son scope ;
> ses actions (création, seed, statut) écrivent uniquement dans le scope du tenant cible.

> **`liakont.fleet` est HORS de la matrice des rôles éditeur** ci-dessus (OPS04). C'est une
> permission d'**IT Innovations** (l'exploitant), pas de l'éditeur client : elle ouvre le dashboard
> de flotte cross-**instance** sur l'instance mutualisée. Elle est portée par le rôle de realm dédié
> **`exploitant`** (§2), mappé `exploitant → liakont.fleet` dans `RolePermissionCatalog` (ADR-0017) —
> distinct des rôles `lecture`/`operateur`/`parametrage`/`superviseur` : un utilisateur éditeur ne la
> reçoit jamais, et un `exploitant` ne reçoit aucune permission éditeur. La télémétrie de flotte est
> strictement technique (aucune donnée métier d'éditeur).

## 4. Utilisateurs de test (realm `liakont-dev`)

Un utilisateur de test par rôle, mot de passe `Test@1234` (non temporaire), e-mail vérifié.

| Utilisateur | Rôles |
|---|---|
| `lecture@liakont.local` | `lecture` |
| `operateur@liakont.local` | `lecture`, `operateur` |
| `parametrage@liakont.local` | `lecture`, `operateur`, `parametrage` |
| `superviseur@liakont.local` | `lecture`, `operateur`, `parametrage`, `superviseur` |
| `exploitant@liakont.local` | `exploitant` |

## 5. Consommation

Ces permissions et rôles sont consommés par les items à venir (API, WEB, SUP, OPS) qui
protègent leurs endpoints et leurs pages via les politiques d'autorisation du Host.

- Les **rôles** sont portés par le realm Keycloak et arrivent dans le jeton via le mapper
  `realm roles` (claim `roles`).
- Les **permissions** sont déclarées dans `LiakontPermissions` et cataloguées par
  `ReflectionPermissionCatalog`.
- L'**IdP** est consommé derrière l'abstraction de la décision **D10**
  (`IIdentityProviderAuthenticator`, `src/Host/Liakont.Host/Security/Abstractions/`) :
  Keycloak est UNE implémentation (`KeycloakIdentityProviderAuthenticator`), une alternative
  in-process (ex. OpenIddict) se branche sans toucher au reste du Host. Aucun appel
  IdP-spécifique n'existe hors de la couche d'auth.

## 6. Population des claims de permission (au sign-in)

Les permissions Liakont sont **entièrement dérivées des rôles** (matrice §3) : un utilisateur n'a
d'autres permissions que celles que ses rôles realm lui accordent. Le pont rôle→permission est posé
**au sign-in**, dans la couche d'auth du Host (derrière l'abstraction D10), et matérialise la
matrice §3 en un **catalogue immuable** (code Liakont, jamais `Stratum.*`) :

- À l'ouverture de session OIDC, les rôles realm du principal (claim `roles`) sont projetés en
  permissions via ce catalogue, **émises comme claims `permission`** sur le principal.
- L'UI (`ClaimsPermissionService`) **et** les endpoints (`PermissionAuthorizationHandler`) lisent
  ce même claim `permission` — un seul mécanisme d'autorisation produit, sans requête base par
  contrôle. Le court-circuit super-admin (`SuperAdminRoles`) reste inchangé.
- **Caveat révocation** : la garde endpoint lisant des claims figés au sign-in (cookie en expiration
  glissante), une révocation de rôle n'est pas honorée immédiatement — fenêtre **non bornée pour une
  session active**, détaillée dans ADR-0017 (§Négatif). Compromis accepté au merge ou atténué.
- Un IdP alternatif (décision D10) réutilise la même projection : aucun appel IdP-spécifique hors
  de la couche d'auth.

> Sans cette projection, aucun claim `permission` n'est posé sous OIDC et tout l'UI permission-gated
> est masqué pour les rôles non super-admin (les contrôles passent en super-admin, qui court-circuite
> la garde — d'où un défaut latent jusqu'au premier écran exigeant un rôle élevé non super-admin).

Conception : `docs/adr/ADR-0017-pont-role-permission-claims-oidc.md` (item producteur : `IDN01`).

## 7. Note de divergence — ADR-0013 socle (« RBAC unchanged / Neutral »)

> Note **Liakont** (RDF12, 2026-06-20). L'ADR socle `docs/adr/socle/ADR-0013-keycloak-identity-provider.md`
> n'est **PAS modifié** ici (règle du socle vendored : `blueprint.md` §4, `socle/README.md` — un ADR socle
> ne se rouvre pas) ; cette note consigne la nuance côté Liakont.

L'ADR-0013 socle affirme en §**Neutral** que « **Stratum's RBAC is unchanged** — downstream modules see the
same `IActorContext` and permission model » et, dans son flux, que « `PermissionPolicyProvider` checks
Stratum's **Grant table** (unchanged) ». Sous l'authentification **OIDC** de Liakont, cette affirmation est
**partiellement superseded** :

- Les permissions Liakont **ne sont plus dérivées de la table `Grant`** du socle : elles sont **recréées**
  par projection **rôle realm → claim `permission`** au sign-in (pont **ADR-0017**, livré par **IDN01** —
  voir §6 ci-dessus). Le contrôle d'autorisation lit le **claim `permission`** du principal, pas la table
  `Grant`.
- Ce qui **reste vrai** d'ADR-0013 : l'abstraction `IActorContext` est inchangée (les modules consomment
  toujours le même contrat d'acteur) et le **modèle** permission-based (politiques par permission) est
  conservé — seule la **source** des permissions change (claims OIDC projetés, et non la table `Grant`).
- Conséquence à garder en tête : le **caveat de révocation** (claims figés au sign-in) propre au chemin
  OIDC (§6, ADR-0017 §Négatif, fenêtre bornée pour les permissions **sensibles** par RDF10) n'existait pas
  dans le modèle « Grant table » du socle. C'est une divergence **assumée** de Liakont, pas un bug du socle.

Le mapping realm↔tenant laissé « ouvert » par ce même ADR socle (« 1:1 vs shared realm with attributes ») est,
lui, tranché par `docs/adr/ADR-0021-realm-keycloak-unique-isolation-par-claim.md` (realm **unique partagé**).
