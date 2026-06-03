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

## 4. Utilisateurs de test (realm `liakont-dev`)

Un utilisateur de test par rôle, mot de passe `Test@1234` (non temporaire), e-mail vérifié.

| Utilisateur | Rôles |
|---|---|
| `lecture@liakont.local` | `lecture` |
| `operateur@liakont.local` | `lecture`, `operateur` |
| `parametrage@liakont.local` | `lecture`, `operateur`, `parametrage` |
| `superviseur@liakont.local` | `lecture`, `operateur`, `parametrage`, `superviseur` |

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
