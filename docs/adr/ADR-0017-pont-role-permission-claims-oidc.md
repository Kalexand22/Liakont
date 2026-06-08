# ADR-0017 — Pont rôle→permission sous OIDC : population des claims de permission au sign-in

- **Statut** : Proposé (2026-06-08).
- **Date** : 2026-06-08
- **Contexte décisionnel** : `orchestration/items/WEB.yaml` (WEB05, bloqué le 2026-06-08),
  session-log WEB05 (diagnostic sur pièce : E2E opérateur 5/7, boutons permission-gated masqués),
  `docs/architecture/identity-permissions-liakont.md` (matrice §3 = source de vérité),
  `src/Host/Liakont.Host/Security/ClaimsPermissionService.cs` (garde UI = claims `permission`),
  `src/Host/Liakont.Host/Security/PermissionAuthorizationHandler.cs` (garde endpoint = base),
  `src/Host/Liakont.Host/Security/Keycloak/KeycloakIdentityProviderAuthenticator.cs` (pipeline
  OIDC, `OnTokenValidated`), `src/Modules/Identity/Infrastructure/UserSyncService.cs` (provisioning :
  rôle par défaut uniquement), `docs/adr/socle/ADR-0013-keycloak-identity-provider.md`,
  `docs/adr/ADR-0002-empreinte-idp-keycloak-vs-openiddict.md`, `CLAUDE.md` n°2 (aucune règle
  inventée), n°6 (abstraction IdP D10), n°9 (tenant-scope), n°19 (pages permission-gated).

## Contexte

L'autorisation produit a **deux consommateurs de permissions**, et aucun n'est alimenté sous OIDC :

1. **L'UI Blazor** passe par `IPermissionService` → `ClaimsPermissionService`, qui lit
   **uniquement les claims de type `"permission"`** du principal (`ClaimsPermissionService.cs`,
   `RefreshPermissions`). C'est cette garde qui masque/affiche boutons, nav et pages.
2. **Les endpoints serveur** passent par `PermissionRequirement` → `PermissionAuthorizationHandler`,
   qui lit **la base** via `IIdentityQueries.UserHasPermission(userId, permission)`.

Sous OIDC, `OnTokenValidated` appelle `UserSyncService.SyncFromOidcClaimsAsync`, qui ne pose que
le **rôle par défaut** local (`lecture`) et ne projette pas les rôles realm Keycloak en permissions.
Résultat : **aucun claim `"permission"` n'est émis** (la garde UI est à sec) **et aucun grant
rôle→permission n'est synchronisé en base** (la garde endpoint l'est aussi). Le seul endroit qui
sait poser ces claims aujourd'hui est le endpoint **test-login** du Host — preuve que le transport
attendu côté UI est bien des **claims posés à l'ouverture de session**.

Le défaut est **latent** parce que les deux gardes **court-circuitent pour les super-admin**
(`SuperAdminRoles.IsSuperAdmin`). Tous les E2E antérieurs (dont la nav Supervision de WEB01)
tournaient en super-admin → ils passaient sans jamais exercer le pont rôle→permission. **WEB05 est
le premier item dont l'E2E exige un élément permission-gated visible pour un rôle élevé NON
super-admin** ; il a donc révélé le trou (E2E opérateur 5/7, boutons masqués par timeout).

La matrice **§3** de `identity-permissions-liakont.md` (4 rôles × 4 permissions) est la **source de
vérité** documentée et saine : les permissions Liakont sont **entièrement dérivées des rôles** (un
utilisateur n'a d'autres permissions que celles que ses rôles lui accordent). Trancher *comment* et
*où* projeter rôle→permission est une **décision d'architecture** touchant la couche d'auth et la
frontière D10 (abstraction IdP) — d'où cet ADR, et non un correctif de WEB05.

## Décision

### 1. Source de vérité unique : la matrice §3, matérialisée en catalogue immuable

Le mapping rôle→permission est matérialisé en un **catalogue immuable** dans la couche d'auth du
Host (`src/Host/Liakont.Host/Security/`, code **Liakont-spécifique**, jamais `Stratum.*`), unique
représentation en code de la matrice **§3** de `identity-permissions-liakont.md`. **Aucune valeur
n'est inventée** : les 4 rôles (`lecture`, `operateur`, `parametrage`, `superviseur`) et les 4
permissions (`liakont.read/actions/settings/supervision`) proviennent du document.

### 2. Projection rôle→permission au sign-in OIDC, émise en claims `"permission"`

À l'ouverture de session OIDC, **dans la couche d'auth derrière l'abstraction D10**
(`KeycloakIdentityProviderAuthenticator` / une `IClaimsTransformation`), les rôles realm du
principal (claim `roles`) sont projetés en permissions via le catalogue, **émises comme claims de
type `"permission"`** sur le principal — exactement le transport que `ClaimsPermissionService` (UI)
et le chemin test-login consomment déjà. Aucun appel IdP-spécifique n'existe hors de cette couche :
un IdP alternatif (décision D10, ex. OpenIddict) réutilise la même projection.

### 3. La garde endpoint lit les MÊMES claims `"permission"`

`PermissionAuthorizationHandler` lit le **même claim `"permission"`** du principal (au lieu d'une
requête base par appel). Comme les permissions Liakont sont **100 % dérivées des rôles** (§3), les
claims projetés sont **complets et autoritatifs** : la **garde** d'autorisation est unique (UI et
endpoints consomment le même claim), pas de hit base par requête, **aucun fichier `Stratum.*`
modifié**. Le court-circuit super-admin (`SuperAdminRoles.IsSuperAdmin`) reste inchangé. Le chemin
base (`IIdentityQueries.GetUserPermissions` / `identity.grants`) n'est **pas retiré** : il reste
consommé par le harnais `/auth/test-login` (non-OIDC), qui doit demeurer cohérent avec le catalogue
§3 (voir INV-IDN01-6).

## Invariants

- **INV-IDN01-1** — Les permissions dérivent EXCLUSIVEMENT du catalogue matérialisant la matrice
  §3 ; aucun mapping rôle→permission n'existe hors de ce document (anti « règle inventée »,
  `CLAUDE.md` n°2).
- **INV-IDN01-2** — La projection rôle→permission vit dans la couche d'auth derrière l'abstraction
  D10 ; aucun appel IdP-spécifique ne fuit (`CLAUDE.md` n°6).
- **INV-IDN01-3** — UI (`ClaimsPermissionService`) et endpoints (`PermissionAuthorizationHandler`)
  lisent le même claim `"permission"` du principal : la **garde** d'autorisation est unique. La
  **population** du claim a deux points d'entrée — la projection au sign-in OIDC et le harnais
  `/auth/test-login` (non-OIDC) — qui doivent tous deux rester cohérents avec le catalogue §3
  (voir INV-IDN01-6).
- **INV-IDN01-4** — Un rôle élevé NON super-admin voit EXACTEMENT les éléments permission-gated que
  ses rôles accordent, prouvé par E2E pour les 4 utilisateurs de test (§4).
- **INV-IDN01-5** — Aucun fichier `Stratum.*` vendored modifié ; aucune validation Blocking
  affaiblie ; aucun E2E désactivé pour passer au vert ; aucun secret en clair.
- **INV-IDN01-6** — IDN01 ne supprime PAS le chemin base (`IIdentityQueries.UserHasPermission` /
  `GetUserPermissions` / `identity.grants`) dont dépend `/auth/test-login` : soit les grants
  restent seedés, soit `test-login` est routé sur la même projection §3. Un test asserte que
  `test-login` émet toujours des claims `"permission"` corrects et NON vides (anti-faux-vert : pas
  de code orphelin, pas de harnais silencieusement cassé).

## Conséquences

**Positif** : WEB05 et toute la surface permission-gated (nav Supervision de WEB01, WEB03b/c,
WEB07b, WEB09, SUP02…) fonctionnent sous OIDC pour les rôles non super-admin ; source unique = la
matrice §3 ; un seul mécanisme (claims) pour l'UI et les endpoints ; pas de hit base par requête ;
aucune abstraction inventée, aucun `Stratum.*` modifié ; les tests prouvent la **visibilité réelle**
(anti-faux-vert : on ne désactive pas l'E2E, on rend l'élément réellement visible pour le rôle).

**À la charge d'IDN01** : implémenter la projection (`OnTokenValidated` / `IClaimsTransformation`)
et le catalogue immuable §3, aligner `PermissionAuthorizationHandler` sur les claims, écrire et
**exécuter** les tests d'intégration (jeton → rôles → claims → autorisation, pour les 4 rôles) et
un E2E sur une surface permission-gated **déjà existante** au moment d'IDN01 (nav Supervision de
WEB01, actions de WEB03b…) prouvant qu'un rôle élevé non super-admin voit ses éléments. La
re-validation de l'E2E opérateur **de WEB05** (7/7) incombe à **WEB05**, qui dépend d'IDN01 — elle
ne peut pas être un critère d'IDN01 (WEB05 s'exécute après).

**Négatif — à accepter consciemment par l'humain (révocation différée)** : en lisant des claims
figés au sign-in plutôt qu'une lecture base *live*, la garde endpoint cesse d'honorer
**immédiatement** une révocation de rôle dans Keycloak. Le cookie d'auth est en **expiration
glissante** (`KeycloakIdentityProviderAuthenticator.cs:198-199` : `SlidingExpiration = true`,
`ExpireTimeSpan = 8h`) : il se ré-émet avec les **mêmes claims figés sans rejouer
`OnTokenValidated`**. La fenêtre de péremption vaut donc **au minimum** 8 h et est en pratique
**non bornée pour une session active** (jusqu'à une ré-authentification complète) — et non « = durée
de vie du cookie ». Sur un produit fiscal, `liakont.actions` (envoi à l'administration) et
`liakont.settings` (paramétrage TVA) sont sensibles : l'opérateur doit accepter cette fenêtre au
merge, OU exiger une atténuation — désactiver/raccourcir `SlidingExpiration`/`ExpireTimeSpan`,
forcer une ré-auth, et/ou conserver une vérification serveur de révocation pour ces deux permissions
sensibles (la garde claims reste pour le reste). Note : la garde **UI** (`ClaimsPermissionService`)
était déjà claims-based, donc déjà soumise à cette fenêtre ; le présent ADR l'étend à la garde
**endpoint**.

**Limite** : la matrice §3 est statique (rôle→permission). Toute permission par-utilisateur ad hoc
(hors rôle) sortirait de ce modèle et exigerait un avenant — non requis par le produit actuel.

## Alternatives rejetées

- **Synchroniser les rôles Keycloak → grants en base (`identity.grants`) dans `UserSyncService`**,
  alimentant la garde base : n'alimente PAS la garde UI claims-based (`ClaimsPermissionService`) →
  WEB05 reste cassée ; ajoute un chemin d'écriture au provisioning ; **deux sources de vérité**
  (claims pour l'UI, base pour les endpoints). **Rejetée.**
- **Faire lire la base à `ClaimsPermissionService` à chaque rendu** : hit base par contrôle de
  permission dans l'UI, touche l'abstraction socle, plus lent et plus fragile. **Rejetée.**
- **Coder un mapping rôle→permission séparé du document §3** : duplique la source de vérité, dérive
  dans le temps, risque de « règle inventée » (`CLAUDE.md` n°2). **Rejetée** — le catalogue est
  l'unique matérialisation de §3.

## Références

- `docs/architecture/identity-permissions-liakont.md` §3 (matrice), §4 (utilisateurs de test), §6 (mécanisme)
- `src/Host/Liakont.Host/Security/` : `ClaimsPermissionService.cs`, `PermissionAuthorizationHandler.cs`,
  `LiakontPermissions.cs`, `SuperAdminRoles.cs`, `Keycloak/KeycloakIdentityProviderAuthenticator.cs`
- `src/Modules/Identity/Infrastructure/UserSyncService.cs` (provisioning OIDC)
- `docs/adr/socle/ADR-0013-keycloak-identity-provider.md` ; `docs/adr/ADR-0002-empreinte-idp-keycloak-vs-openiddict.md`
- `CLAUDE.md` n°2 / n°6 / n°9 / n°19 ; `orchestration/items/WEB.yaml` (WEB05) ; `orchestration/items/IDN.yaml` (IDN01)
