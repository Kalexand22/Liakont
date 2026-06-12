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
| `liakont.supervision` | `LiakontPermissions.Supervision` | Supervision : vues cross-tenant en lecture seule (module Supervision). |
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

Résumé : `lecture` → read ; `operateur` → read + actions ; `parametrage` → read + actions
+ settings ; `superviseur` → les quatre permissions.

> La supervision cross-tenant reste en **lecture seule** (CLAUDE.md n.9 : seul le module
> Supervision a des vues cross-tenant ; toute autre requête métier est tenant-scopée).

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
