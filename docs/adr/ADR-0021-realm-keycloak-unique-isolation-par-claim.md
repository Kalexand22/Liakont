# ADR-0021 — Realm Keycloak unique partagé : isolation des tenants par claim `company_id`

- **Statut** : **Accepté** (2026-06-13).
- **Date** : 2026-06-13
- **Nature** : cet ADR **précède** le chantier d'implémentation (multi-lots, non démarré). Les sections
  **Décision** et **Invariants** sont **normatives** : elles décrivent la **cible** à atteindre, pas
  l'état du code. L'encadré « État actuel vs cible » et la section « Séquencement de construction »
  distinguent ce qui existe déjà de ce qui reste à construire. Aucun invariant n'est garanti tant que
  le séquencement n'est pas livré **et** prouvé par test.
- **Contexte décisionnel** : recette interactive OPS03 (provisioning multi-tenant) — un utilisateur
  de tenant fraîchement provisionné ne peut pas se connecter ; `docs/adr/socle/ADR-0013-keycloak-identity-provider.md`
  (le mapping realm↔tenant « 1:1 vs shared realm with attributes » est laissé comme décision de
  **déploiement**, pas de code) ; `src/Common/Infrastructure/Database/TenantProvisioningService.cs`
  (realm `stratum-{tenantId}` codé en dur) ; `src/Common/Infrastructure/Keycloak/KeycloakRealmProvisioner.cs`
  (création realm + client `stratum` + redirect URIs par sous-domaine) ;
  `src/Host/Liakont.Host/Security/Keycloak/KeycloakIdentityProviderAuthenticator.cs` (handlers de
  login OIDC **statiques** : realm primaire + `AdditionalRealms`, rien pour les realms provisionnés) ;
  `src/Host/Liakont.Host/Security/Keycloak/KeycloakTenantUserProvisioner.cs` (le lot A pose déjà
  l'attribut `company_id` par utilisateur, **en source secondaire**) ; `docs/architecture/provenance-socle-stratum.md` §4.24
  (le mapper `company_id` a été figé au niveau **client** pour éviter un claim absent) et §4.26
  (retrait de l'admin de realm répliqué) ; `docs/adr/ADR-0002-empreinte-idp-keycloak-vs-openiddict.md`,
  `docs/adr/ADR-0017-pont-role-permission-claims-oidc.md` ; `CLAUDE.md` n°6 (abstraction IdP D10),
  n°9 (tenant-scope), n°10/18 (secrets chiffrés, jamais en clair ni journalisés).

## Contexte

Le provisioning de tenant hérité du socle Stratum crée **un realm Keycloak par tenant**
(`stratum-{tenantId}`, client OIDC `stratum`), alors que le realm principal est `liakont-dev` /
client `liakont`. Trois constats convergents :

1. **Décision jamais prise pour Liakont.** L'ADR socle ADR-0013 énonce explicitement que le mapping
   realm↔tenant — *« 1:1 vs shared realm with attributes »* — est **une décision de déploiement, pas
   de code**. Aucun ADR Liakont (0001→0018) ne tranche ce choix. Le realm-par-tenant est donc un
   **défaut du socle codé en dur** (`TenantProvisioningService.cs` : `realmName = "stratum-{tenantId}"`),
   pas une décision Liakont assumée.

2. **Le login tenant n'est pas construit.** `KeycloakIdentityProviderAuthenticator` n'installe des
   handlers de login (challenge OIDC) que pour le realm **primaire** + les realms **statiques** de
   `AdditionalRealms` (vide en dev). Les realms provisionnés sont enregistrés pour la **validation**
   des jetons (`IRealmRegistry`, dynamique) mais **n'ont aucun handler de login**. Un utilisateur de
   `stratum-{tenantId}` n'a donc **aucun chemin de connexion** : il voit la page du realm primaire,
   où il n'existe pas (« identifiant invalide »). Le seul indice de design (redirect URIs par
   sous-domaine) **ne pilote aucun challenge** : `AddTenantRedirectUriAsync` ajoute bien une
   redirect-URI de sous-domaine au client du realm **primaire**, mais aucun handler OIDC ne route
   l'utilisateur vers la page de login du realm provisionné.

3. **L'isolation ne repose déjà PAS sur le realm.** L'isolation des données est portée par le claim
   `company_id` (mapper du realm) **+ le scoping tenant de toute requête métier** (`CLAUDE.md` n°9).
   Le realm-par-tenant ajoute une lourde mécanique (un realm + client + secret + sous-domaine +
   handler dynamique par client, DNS/TLS wildcard, et une taxe permanente « appliquer chaque
   changement de config à N realms ») pour une isolation **déjà assurée à la couche données**. Ce
   coût est contraire au GTM volume (beaucoup de petits clients par connecteur). Le seul vrai upside
   (SSO/politiques d'auth **par client**) relève d'un déploiement **dédié** mono-tenant.

Trancher le modèle de realm touche la couche d'auth et la frontière D10 (abstraction IdP) : c'est
une **décision d'architecture**, d'où cet ADR.

## Décision

> Les points ci-dessous sont **normatifs** (la cible). Les leviers Keycloak nommés (mapper d'attribut,
> User Profile read-only, `trustEmail`, `otpPolicy`…) sont les mécanismes standard à employer ; le
> détail exact est arrêté au chantier, mais le **résultat exigé** est celui décrit ici.

### 1. Un seul realm Keycloak partagé `liakont`

Tous les utilisateurs de tous les tenants vivront dans un **realm unique** (`liakont-dev` en dev,
`liakont` en prod). Le provisioning de tenant **ne créera plus de realm ni de client par tenant**.

La capacité socle de provisioning realm-par-tenant (`KeycloakRealmProvisioner`) **sort du chemin de
création de tenant**. ⚠️ Précision de frontière : aujourd'hui ce provisioner **n'est pas** « derrière
l'abstraction IdP D10 » (`IIdentityProviderAuthenticator`) — `TenantProvisioningService` en dépend
**directement** via `IKeycloakRealmProvisioner` (abstraction *Keycloak-spécifique*, située dans
`Common.Infrastructure`, hors de la couche d'auth `Security/`). C'est une interface **substituable en
DI**, mais l'appel est inline dans `ProvisionAsync`, sans seam de désactivation. La sortie se fait
donc soit par une **implémentation no-op** enregistrée en DI pour le profil SaaS partagé (le
déploiement **dédié** mono-tenant garde l'implémentation Keycloak), soit par retrait du bloc inline.
**Résultat exigé** : **aucun `POST /admin/realms`** n'est émis dans le chemin de création SaaS
partagé (garde/test). La capacité reste disponible pour le déploiement dédié.

### 2. Isolation par claim `company_id` (par-utilisateur, immuable) + cross-check applicatif

L'isolation tenant côté identité reposera sur :

- **(a)** le claim `company_id` porté par **chaque** utilisateur, **émis depuis un attribut
  par-utilisateur** (mapper `oidc-usermodel-attribute-mapper`), **non vide**, **distinct entre
  tenants**, **non éditable par l'utilisateur** et **immuable** après création.
  ⚠️ **Rappel §4.24 — point dur** : en realm partagé, `company_id` **ne peut plus** être figé au
  niveau **client** (un client unique sert tous les tenants → tous les jetons porteraient la **même**
  valeur = isolation nulle). Le mapper `oidc-hardcoded-claim-mapper` au niveau client présent dans
  `realm-export.json` (valeur fixe de dev) **doit être retiré** et remplacé par le mapper d'attribut.
  L'immuabilité doit être **garantie côté IdP** (déclaration `company_id` dans le User Profile en
  lecture seule / édition admin-only, self-service de profil désactivé) — ce n'est **pas** acquis
  aujourd'hui (le lot A pose l'attribut, mais en **source secondaire** et **écrasable**), c'est un
  livrable du chantier.
- **(b)** un **cross-check applicatif** : middleware **global** *fail-closed* qui, pour **toute**
  requête authentifiée **d'un utilisateur de tenant**, exige la présence du claim `company_id` **et**
  son égalité avec le `company_id` du tenant résolu ; absence de claim ou divergence ⇒ **rejet** (403
  / déconnexion), jamais « servir quand même ». ⚠️ La comparaison porte sur le **`company_id`** du
  tenant résolu, **pas** sur le `tenant_id` (deux axes distincts : *routage de base* par `tenant_id`
  vs *filtre de lignes* par `company_id`). **L'opérateur d'instance (super-admin) est exempté** : il
  accède en cross-tenant à `/supervision` et `/clients` et n'a **pas** de `company_id` — via le **même
  court-circuit** que `TenantSuspensionMiddleware` (`SuperAdminRoles.IsSuperAdmin`, cf.
  `TenantSuspensionMiddleware.cs:37`). Sans cette exemption, le cross-check contredirait l'invariant
  « super-admin hors périmètre tenant ». **Périmètre** : le cross-check porte sur le principal
  **OIDC/cookie** (utilisateur de tenant, via `SmartDefault` → JwtBearer/Cookie). Le **chemin agent**
  (`X-Agent-Key`, `AgentApiAuthenticationFilter` — « une clé = un agent = un tenant ») est **hors
  périmètre** : il résout son tenant **depuis la clé API scopée**, n'a **pas** de claim `company_id` et
  conserve son propre scoping ; une implémentation naïve « toute requête authentifiée exige
  `company_id` » le **403erait à tort**.
- **(c)** **Résolution du tenant courant pilotée par le jeton (fail-closed *belt-and-suspenders*).**
  En realm unique, la chaîne actuelle `subdomain > header > JWT claim` (`CompositeTenantResolver.cs:7`)
  ne distingue plus rien côté IdP (un seul realm). La résolution **autoritaire** devient
  `company_id(jeton) → outbox.tenants.company_id → tenant` ; les voies **client-fournies**
  (sous-domaine, header `X-Tenant-Id`) **ne sont plus autoritaires**. Posture explicite : un indice de
  tenant client-fourni qui **contredit** le jeton ⇒ **403** (jamais servi silencieusement comme tenant
  du jeton) — ainsi une réintroduction future d'un header « de confiance » ne peut pas fuir en
  silence ; c'est ce *403-sur-contradiction* qu'assert le test d'INV-0021-4. Deux préconditions
  **structurelles** à graver :
  - `outbox.tenants.company_id` doit être **UNIQUE** — le résolveur est faux si deux tenants
    partagent la valeur ; aujourd'hui c'est un `Guid.NewGuid` (unicité **probabiliste**, pas
    structurelle) sur une colonne **nullable sans contrainte** (`V016__add_company_id_to_tenants.sql`) ;
  - le tenant historique **`default`** a `company_id = NULL` en base → à **backfiller**, sinon le
    résolveur plante pour lui.

Le contrôle d'isolation **primaire** reste le **scoping tenant des requêtes métier** (`CLAUDE.md`
n°9) ; le claim immuable + le cross-check sont la **défense en profondeur** qui remplace la frontière
de realm.

### 3. 2FA forcé sur le login mot de passe

Le realm **imposera** l'enrôlement et la présentation d'un second facteur sur le flux **email/mot de
passe** (TOTP au minimum, WebAuthn/passkey en option) — via `browser flow` imposant le 2ᵉ facteur,
`requiredAction CONFIGURE_TOTP` et `otpPolicy` dans `realm-export.json`. Pour un login **brokered**
(SSO), on s'appuie sur le MFA de l'IdP externe (pas de double-prompt) ; un step-up Liakont reste
possible par politique ultérieure.

### 4. Brokering multi-SSO **lié par email vérifié**, sans auto-création

Le realm exposera, à côté du login email/mot de passe, un jeu de providers SSO (**Google** et
**Microsoft/Entra** au départ, extensible). Le **first-broker-login** **reliera l'identité externe à
un compte Liakont DÉJÀ provisionné, matché par email** ; il **n'auto-créera JAMAIS** de compte — un
compte auto-créé serait **orphelin de `company_id`** (trou d'isolation).

Garde-fous **obligatoires** :

- la liaison n'a lieu que si l'email de l'IdP externe est **vérifié** (`email_verified=true` du jeton
  externe ; `trustEmail=false` sur le provider + étape « Verify existing account by email ») — ne
  jamais faire confiance à un email non vérifié pour matcher (sinon prise de contrôle de compte
  cross-IdP) ;
- le chemin brokered **déclenche la même projection rôle→permission** (ADR-0017, `OnTokenValidated` /
  `IClaimsTransformation`) que le login mot de passe **et** émet le claim `company_id` par-utilisateur,
  pour ne pas créer un chemin de login sans claims d'autorisation/isolation.

Les secrets de chaque provider sont de la **config par environnement / secret manager de
l'orchestrateur**, **jamais** versionnés (en particulier **jamais** dans `realm-export.json`, qui est
dans le dépôt) ni journalisés (`CLAUDE.md` n°10/18).

### 5. Email obligatoire et unique

Le realm exigera un **email par utilisateur, unique** (`loginWithEmailAllowed=true`,
`duplicateEmailsAllowed=false`, email requis). L'email est la **clé de liaison** du brokering SSO et
garantit l'unicité d'identité dans le realm partagé.

## État actuel vs cible

| Élément | Déjà en place | À construire (chantier) |
|---|---|---|
| Realm partagé | realm `liakont-dev` + handler OIDC primaire ; `IRealmRegistry` (validation jetons) | retrait realm/client par tenant du chemin de création (no-op DI ou retrait bloc) + garde « aucun `POST /admin/realms` » |
| `company_id` | mappé côté OIDC + posé en **attribut par-utilisateur** au provisioning (source **secondaire**) ; source canonique = **mapper hardcodé niveau client** ; comptes **déjà seedés** sans attribut | mapper **d'attribut** par-utilisateur (source **primaire**) ; **retrait** du mapper hardcodé du `realm-export.json` ; attribut **immuable / read-only** côté IdP ; **backfill** de l'attribut sur les comptes seedés (hors super-admin) |
| Résolution de tenant | `subdomain > header > JWT claim` (`CompositeTenantResolver`) ; `outbox.tenants.company_id` **nullable, non-UNIQUE** (`Guid.NewGuid`) ; tenant `default` = `NULL` | résolveur `company_id(jeton) → tenant` **autoritaire** (403 si indice client contredit) ; contrainte **UNIQUE** sur `company_id` ; backfill du `default` |
| Cross-check | **absent** — `tenant_id` (routage) et `company_id` (filtre) ne sont jamais confrontés | middleware **global fail-closed** (principal **OIDC/cookie** ; agent `X-Agent-Key` **hors périmètre**) ; **exempte le super-admin** (`SuperAdminRoles.IsSuperAdmin`, comme `TenantSuspensionMiddleware`) |
| 2FA | non configuré dans `realm-export.json` | `browser flow` 2FA + `CONFIGURE_TOTP` + `otpPolicy` |
| Brokering SSO | absent | providers Google/Microsoft, first-broker-login lié par email **vérifié**, claims `permission` + `company_id` sur ce chemin |
| Super-admin d'instance | réplication dans les realms tenant **retirée** (commit `0e4c10a`) | statut explicite dans le realm partagé : compte de plateforme **hors périmètre tenant**, jamais résolu vers un tenant |
| Comptes de recette | `testtenant2` / `marie` dans des realms `stratum-*` (dev, jetables) | recréés dans le realm partagé |

## Invariants

> Ces invariants sont la **cible** à satisfaire ; chacun nomme sa **preuve** attendue. Indexation sur
> le numéro d'ADR (et non sur un item d'orchestration) car cet ADR précède un chantier multi-lots.

- **INV-0021-1** — Un seul realm Keycloak héberge tous les tenants ; le provisioning de tenant ne
  crée ni realm ni client par tenant. *Preuve* : garde/test prouvant qu'aucun `POST /admin/realms`
  n'est émis dans le chemin de création SaaS partagé.
- **INV-0021-2** — Tout utilisateur **de tenant** (provisionné **ou déjà seedé** dans `liakont-dev`)
  porte un `company_id` **non vide** et **distinct entre tenants** ; l'unicité est **structurelle**
  (contrainte **UNIQUE** sur `outbox.tenants.company_id`), pas seulement probabiliste (`Guid.NewGuid`).
  Le super-admin d'instance en est **exclu** (sans `company_id`, cf. INV-0021-4). *Preuve* : test
  prouvant (a) aucun utilisateur de tenant sans `company_id` (anti mode de panne §4.24), (b) deux
  utilisateurs de tenants différents portent des `company_id` différents (anti mapper hardcodé),
  (c) la contrainte UNIQUE rejette un doublon.
- **INV-0021-3** — Le `company_id` est **immuable et non éditable par l'utilisateur** (User Profile
  read-only / admin-only, self-service de profil désactivé). *Preuve* : test qu'un appel Account API
  tentant de modifier `company_id` est refusé.
- **INV-0021-4** — **Cross-check fail-closed** (principal **OIDC/cookie** ; le chemin **agent**
  `X-Agent-Key` est **hors périmètre**, cf. §2b) : une requête d'un **utilisateur de tenant** sans
  claim `company_id`, ou dont le `company_id` ≠ `company_id` du tenant résolu, est **rejetée** (403).
  Le tenant résolu **dérive du jeton** (§2c) ; un indice de tenant **client-fourni** (sous-domaine /
  header) qui **contredit** le jeton ⇒ **403** (jamais servi silencieusement). Le **super-admin
  d'instance est exempté** (`SuperAdminRoles.IsSuperAdmin`, même court-circuit que
  `TenantSuspensionMiddleware.cs:37`) : accès cross-tenant légitime, sans `company_id`. *Preuve* : test
  « jeton tenant A + Host/Header tenant B ⇒ 403 », « super-admin sans `company_id` ⇒ accès
  `/supervision` + `/clients` » **et** « requête agent (`X-Agent-Key`) sans `company_id` ⇒ servie ». Le
  contrôle primaire (scoping métier, `CLAUDE.md` n°9) est inchangé.
- **INV-0021-5** — Le brokering SSO ne relie qu'à un compte **pré-provisionné** (match email avec
  **`email_verified=true`** de l'IdP externe) ; **jamais d'auto-création** ; un compte sans
  `company_id` n'accède à aucune donnée de tenant. *Preuve* : E2E « broker email inconnu ⇒ pas
  d'auto-création » et « broker email non vérifié ⇒ liaison refusée ».
- **INV-0021-6** — **Tout** chemin de login (mot de passe **et** brokered) déclenche la projection
  rôle→permission (ADR-0017) et émet `company_id` ; un login sans claim `permission` ou sans
  `company_id` est un défaut **bloquant**. *Preuve* : test des claims présents sur les deux chemins.
- **INV-0021-7** — 2FA **imposé** sur le login mot de passe. *Preuve* : test qu'un login sans 2FA
  enrôlé force l'enrôlement.
- **INV-0021-8** — Email **obligatoire et unique** (`loginWithEmailAllowed=true`,
  `duplicateEmailsAllowed=false`). *Preuve* : config realm vérifiée + test de provisioning.
- **INV-0021-9** — La couche d'auth reste **derrière l'abstraction IdP** (`CLAUDE.md` n°6) ; le
  middleware de cross-check lit `company_id` via l'abstraction d'acteur (`IActorContext`), **jamais**
  via un type Keycloak (NetArchTest) ; aucun secret provider versionné ni journalisé (n°10/18) ;
  aucune validation Blocking affaiblie ; aucun E2E désactivé pour passer au vert.
- **INV-0021-10** — Pas d'auto-inscription publique : les comptes restent **provisionnés par
  l'opérateur** (assistant OPS03) ; email/mot de passe et SSO sont deux **méthodes de connexion** de
  ces comptes.

## Séquencement de construction

Ordre **sûr** pour ne pas valider l'isolation sur un **faux-vert** (ce n'est **pas** un plan de
migration de production — `testtenant2`/`marie` sont des comptes de dev jetables, simplement recréés) :

1. **`realm-export.json` partagé** : retirer le mapper `company_id` **hardcodé** (niveau client) et le
   remplacer par un mapper **d'attribut** par-utilisateur ; ajouter le `browser flow` 2FA et les
   providers de brokering.
2. **Attribut `company_id` par-utilisateur immuable** (User Profile read-only) comme source primaire,
   **posé aussi sur les comptes déjà seedés** de `liakont-dev` (les 5 rôles de dev), pas seulement sur
   les futurs provisionnés ; le **sysadmin/super-admin reste sans `company_id`** (exempté, §2b).
3. **Registre de résolution** : contrainte **UNIQUE** sur `outbox.tenants.company_id` + **backfill**
   du tenant `default` (`NULL` aujourd'hui) ; résolveur `company_id(jeton) → tenant` autoritaire.
4. **Middleware de cross-check** *fail-closed* (principal OIDC/cookie ; agent `X-Agent-Key` hors
   périmètre ; super-admin exempté) : voie jeton **autoritaire**, **403 si un indice client-fourni
   contredit** le jeton.
5. **Sortie du provisioner realm** du chemin de création (no-op DI ou retrait du bloc inline) + garde
   « aucun `POST /admin/realms` » ; nettoyage du code devenu vestigial (`AddTenantRedirectUriAsync`,
   `RegisterRealm` au provisioning, boucles `AdditionalRealms`/`RealmTenantMap`, seed runtime).
6. **Recréation des comptes de recette** (`testtenant2`/`marie`) dans le realm partagé.

**Règle de séquencement** : ne pas déclarer le multi-tenant partagé « fait » tant que les étapes 1→4
ne sont pas livrées **et** prouvées par test — sinon le premier test multi-tenant passe au **vert** en
masquant l'absence d'isolation (tous les jetons portant la même valeur hardcodée).

## Conséquences

**Positif** : le login tenant **fonctionne immédiatement** (une seule page) ; fin de la prolifération
`stratum-*` et du branding incohérent ; les réglages realm (2FA, email, brokering) se font **une
fois** ; le provisioning est **simplifié** (l'étape « créer le realm + client + secret » disparaît) ;
coût opérationnel réduit (pas de N realms à maintenir, pas de DNS/TLS wildcard ni de challenge OIDC
dynamique, pas de fan-out auth par realm) ; **aligné sur le GTM volume**. Un realm unique offre aussi
une **vue d'audit consolidée** (un même IP attaquant plusieurs comptes est visible d'un coup).

**Négatif — trade-off sécurité accepté consciemment** : on **perd la frontière cryptographique
par-realm** (chaque realm avait sa clé de signature ; un jeton du tenant A était **cryptographiquement
invalide** pour B — un contrôle *fail-closed structurel*, indépendant de tout bug applicatif). En
realm unique, un jeton du tenant A est **cryptographiquement valide partout** ; ce qui distingue A de
B devient la **valeur d'un claim** + la **discipline du cross-check**. On passe donc d'un *fail-closed
structurel* à un contrôle **dépendant de la couverture du cross-check** — d'où son exigence
*fail-closed global* (INV-0021-4) comme **pré-requis bloquant**, et la neutralisation des voies de
résolution client-contrôlées. Le contrôle **primaire** (scoping des requêtes, n°9) est **inchangé**.
Le realm **master** reste « god mode » dans les **deux** modèles (pas de régression sur la
compromission opérateur-plateforme) ; en revanche le realm unique **élargit le blast radius** d'une
compromission au niveau **realm** (hors master, ex. fuite de la clé de signature) d'un tenant à tous —
à compenser par la garde/rotation de la clé de signature et la séparation des rôles d'admin realm
(**durcissement ultérieur, hors périmètre du chantier build** : l'essentiel build-stage est le
cross-check (architecture), pas l'ops de clé). Le **SSO/MFA par client** n'est plus offert dans le
SaaS partagé → réservé au **déploiement dédié**
(réutilisant la capacité socle realm-par-tenant).

**Limite** : un client exigeant son **propre IdP/SSO fédéré** ou des **politiques d'auth propres**
relève d'un **déploiement dédié mono-tenant** ; ce n'est pas une fonctionnalité du SaaS partagé. Le
déclencheur du dédié est une décision **d'offre commerciale**, pas un seuil technique.

## Alternatives rejetées

- **Realm-par-tenant + login dynamique** (sous-domaine → realm, challenge OIDC dynamique par realm,
  DNS/TLS wildcard). *Upside réel à créditer* : une **isolation cryptographique par clé de signature
  de realm** (un jeton de A est invalide pour B **indépendamment** d'un bug de cross-check) — c'est sa
  meilleure propriété de sécurité. *Mais* : l'isolation **primaire** reste le scoping des requêtes
  (n°9), le login n'est **pas construit** et est coûteux à bâtir/maintenir ; **taxe permanente**
  « appliquer chaque changement de config à N realms » (cf. le bug `company_id` §4.24 qui aurait dû
  être backfillé sur chaque realm) ; axe de **sprawl** contraire au GTM volume ; le seul upside
  fonctionnel (SSO par client) est couvert par le **dédié**. **Rejetée** pour le SaaS partagé, en
  acceptant consciemment le trade-off décrit en Conséquences.
- **Realm partagé mais `company_id` figé au niveau client** (comme §4.24) : impossible — un client
  unique sert tous les tenants ; tous les jetons porteraient la **même** valeur. `company_id` **doit**
  être par-utilisateur. **Rejetée.**
- **Auto-création de compte au first-broker-login** (SSO) : produit des comptes **orphelins de
  `company_id`** → trou d'isolation. **Rejetée** au profit de la liaison par email **vérifié** à un
  compte pré-provisionné.

## Références

- **Renvoi réciproque de supersession (RDF12, 2026-06-20)** : cet ADR **supersède partiellement**
  [ADR-0020](ADR-0020-topologie-deploiement-idp.md) (le « **un realm par tenant** » *intra-instance* y
  est remplacé par le realm unique partagé décrit ici — la topologie « Keycloak par instance » d'ADR-0020
  reste, elle, valable) et [ADR-0002](ADR-0002-empreinte-idp-keycloak-vs-openiddict.md) (l'atout
  « réalms multi-tenant, JWKS par realm » de son arbitrage ne décrit plus le modèle SaaS partagé). La
  **frontière cryptographique par-realm** cède la place à l'isolation par **claim `company_id`
  par-utilisateur + cross-check applicatif fail-closed** (trade-off assumé en §Conséquences). Le
  realm-par-tenant n'est pas supprimé : il reste la capacité du **déploiement dédié** mono-tenant.
- `docs/adr/socle/ADR-0013-keycloak-identity-provider.md:141` (mapping realm↔tenant = décision de
  déploiement) ; `docs/adr/ADR-0002-empreinte-idp-keycloak-vs-openiddict.md` ;
  `docs/adr/ADR-0017-pont-role-permission-claims-oidc.md` (projection rôle→permission, à appliquer au
  chemin brokered).
- `src/Common/Infrastructure/Database/TenantProvisioningService.cs` (bloc realm inline à sortir du
  chemin de création) ; `src/Common/Infrastructure/Keycloak/KeycloakRealmProvisioner.cs`.
- `src/Host/Liakont.Host/Security/Keycloak/KeycloakIdentityProviderAuthenticator.cs` (handlers OIDC) ;
  `KeycloakTenantUserProvisioner.cs` (pose `company_id` en attribut, source secondaire) ;
  `src/Host/Liakont.Host/Security/Abstractions/IIdentityProviderAuthenticator.cs` (frontière D10) ;
  `SuperAdminRoles.cs` (court-circuit super-admin).
- `src/Host/Liakont.Host/MultiTenancy/CompositeTenantResolver.cs:7` (chaîne `subdomain > header > JWT`
  à remplacer par la voie jeton) ; `src/Host/Liakont.Host/MultiTenancy/TenantSuspensionMiddleware.cs:37`
  (exemption super-admin à répliquer dans le cross-check) ;
  `src/Common/Infrastructure/Migrations/V016__add_company_id_to_tenants.sql` (`outbox.tenants.company_id`
  nullable, non-UNIQUE — à contraindre + backfiller) ; `AgentApiAuthenticationFilter` /
  `AgentApiHeaders.AgentKey` (`X-Agent-Key`) + `docs/architecture/contrat-agent-v1.md:22` (chemin agent
  « une clé = un agent = un tenant », **hors périmètre** du cross-check).
- `docs/architecture/provenance-socle-stratum.md` §4.24 (mapper `company_id`) / §4.26 (retrait admin
  de realm) ; `deploy/docker/keycloak/realm-export.json` (realm `liakont-dev` = realm partagé ; mapper
  `company_id` hardcodé à retirer).
- `CLAUDE.md` n°6 (abstraction IdP), n°9 (tenant-scope), n°10/18 (secrets).
- *Au passage en « Accepté »* : ajouter un renvoi retour depuis ADR-0013 socle (qui laissait le
  mapping ouvert) et depuis §4.24/§4.26 de la provenance (état hérité superseçu) vers cet ADR.
