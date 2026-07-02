# ADR-0039 — Configuration d'envoi d'emails d'instance : multi-provider (SMTP / Google / O365) chiffrée en base, gérée en console (amende ADR-0018)

- **Statut** : Accepté (2026-07-01) — implémenté (branche `feat/recette-encheres-config-email-instance`).
- **Date** : 2026-07-01
- **Nature** : cet ADR **précède** le chantier d'implémentation et **amende** ADR-0018 (transport SMTP MailKit).
  Il répond à une demande de recette (Karl, 2026-07-01) : pouvoir renseigner **en console** (opérateur
  d'instance, hors tenant) les données d'envoi d'emails, avec une boîte potentiellement **Google ou Office
  365** — paramétrage plus complexe que le simple SMTP basic-auth actuel (aujourd'hui en `appsettings`, non
  chiffré, mono-provider). Sections **Décision**/**Invariants** normatives (cible). Aucune surface fiscale
  (table de config, pas d'audit fiscal) : CLAUDE.md n°1/2/3/4 **N/A** ; le point dur est **le chiffrement des
  secrets** (n°10/18 = P1 si en clair) et **les frontières** (n°6/9/14).

- **Numérotation** : ADR-**0039** (prochain libre après ADR-0038).

- **Contexte décisionnel** : `docs/adr/ADR-0018-transport-smtp-mailkit.md` (SMTP MailKit, basic-auth,
  `appsettings`) — **amendé** ici. Sources code réelles : seam vendored
  `src/Modules/Notification/Contracts/IEmailTransport.cs` + `Infrastructure/Services/StubEmailTransport.cs` ;
  impl Liakont `src/Host/Liakont.Host/Notifications/SmtpEmailTransport.cs` (MailKit `SmtpClient` +
  `AuthenticateAsync(user,pwd)` basic-auth) + `SmtpOptions.cs` ; enregistrement composition root
  `src/Host/Liakont.Host/Startup/AppBootstrap.cs:197-198` (`services.Replace`) ; consommateurs
  `src/Modules/Supervision/Infrastructure/AlertEmailNotifier.cs`,
  `src/Modules/FleetSupervision/Infrastructure/EmailFleetUpdateNotificationSender.cs` ; chiffrement
  `src/Modules/TenantSettings/Application/ISecretProtector.cs` + `PaAccountSecretPurposes.cs` +
  `Infrastructure/DataProtectionSecretProtector.cs` (clés Data Protection niveau **instance**) + patron
  `AddPaAccountHandler.cs` / `GeneriqueAccountResolver.cs:105-112` (déchiffrement en mémoire Host) ; base
  système `src/Common/Infrastructure/Database/ISystemConnectionFactory.cs` + patron
  `src/Modules/FleetSupervision/Infrastructure/PostgresFleetStore.cs` (schéma système) ; XOAUTH2 natif MailKit
  4.17 (`MailKit.Security.SaslMechanismOAuth2`, `Directory.Packages.props:65-66`) ; gabarit form console
  `src/Modules/Notification/Web/Pages/AdminIntegrations.razor` ; permissions
  `src/Host/Liakont.Host/Security/LiakontPermissions.cs`. ADR liés : ADR-0018 (amendé).

## Contexte

La chaîne d'envoi d'emails actuelle (ADR-0018) est **mono-provider, basic-auth, non chiffrée** : le seam
vendored `IEmailTransport` (une méthode `SendAsync`) est implémenté par `SmtpEmailTransport` (MailKit,
`AuthenticateAsync(username, password)`), enregistré au **composition root** via `services.Replace` ; la
config vient de `SmtpOptions` (section `appsettings`), le mot de passe d'une variable d'env non versionnée.

Trois besoins nouveaux : (1) **console** (opérateur d'instance, cross-tenant) pour saisir la config, (2)
support **Gmail / Office 365** (OAuth2, pas seulement basic-auth), (3) **secrets chiffrés au repos en base**
(fini le mot de passe en `appsettings`). Trois pièges à éviter, tous relevés en revue de conformité :

- **Secret en clair = P1** (CLAUDE.md n°10/18) : les secrets (mot de passe SMTP, `client_secret` +
  `refresh_token` OAuth2) doivent être **chiffrés au repos**, jamais dans un DTO de lecture ni un log.
- **Frontières** : `Supervision` est **tenant-scopé, lecture seule cross-tenant** ; y greffer un store
  d'**écriture** cross-instance dilue son invariant (n°9). Le chiffrement (`ISecretProtector`) ne se consomme
  que côté **Host**/TenantSettings (n°6/14).
- **Sur-ingénierie OAuth** : tirer les SDK Google/Microsoft Graph/MSAL pour **envoyer** un email serait
  disproportionné — MailKit fait **nativement** XOAUTH2.

## Décision

### 1. Seam vendored `IEmailTransport` INCHANGÉ ; multi-provider = détail d'impl derrière le seam

On **ne crée pas** un `IEmailSender` concurrent : la multi-provider est un **détail d'implémentation** de
`SmtpEmailTransport`, toujours enregistré au **composition root** via `services.Replace` (ADR-0018 §2). Le
**socle `Stratum.Modules.Notification` n'est PAS modifié** → **aucune entrée de provenance** (CLAUDE.md
n°11/20). Les consommateurs (`AlertEmailNotifier`, `EmailFleetUpdateNotificationSender`) sont inchangés.

### 2. Envoi multi-provider par MailKit XOAUTH2 — AUCUN package neuf

`SmtpEmailTransport` devient **provider-aware** : il charge la config d'instance active (déchiffrée en
mémoire) et branche l'auth sur le **kind déclaré** (`EmailProviderKind` : `SmtpBasic` / `GoogleOAuth2` /
`MicrosoftOAuth2`) — jamais un `if (provider is X)` produit (esprit n°8) :

- `SmtpBasic` → `AuthenticateAsync(user, password)` (actuel) ;
- `GoogleOAuth2` / `MicrosoftOAuth2` → `AuthenticateAsync(new SaslMechanismOAuth2(user, accessToken))`,
  **natif MailKit 4.17** (`smtp.gmail.com:587` / `smtp.office365.com:587` STARTTLS).

**Aucun SDK** (Google/Graph/MSAL). L'acquisition/refresh du token (`grant refresh_token → access_token`) est
derrière une abstraction **`IEmailOAuthTokenProvider`** (Host), impl = `HttpClient` POST sur l'endpoint token
(Google `oauth2.googleapis.com/token` / Microsoft `login.microsoftonline.com/{tenant}/oauth2/v2.0/token`) +
`System.Text.Json` — **aucun package**. Le **consentement interactif** (obtention initiale du `refresh_token`)
est un **concern déploiement** hors code (flux one-time documenté, opérateur colle le `refresh_token`).

### 3. Secrets CHIFFRÉS au repos via `ISecretProtector` existant, sous purposes dédiés

Les **secrets** — `smtp_password`, `oauth_client_secret`, `oauth_refresh_token` — sont **chiffrés** via
l'`ISecretProtector` existant (Data Protection, clés **niveau instance** → déchiffrables au niveau instance
quel que soit le tenant, parfait pour un secret cross-instance) sous des **purposes dédiés** versionnés
(`EmailSecretPurposes.*.v1`, miroir de `PaAccountSecretPurposes`, isolation crypto). **Non-secrets** en clair :
`oauth_client_id` et `oauth_tenant_id` (identifiants d'application/annuaire, pas des secrets) — colonnes
**`oauth_client_id`/`oauth_tenant_id`** (pas `encrypted_*`, pour ne pas laisser une colonne « encrypted » sans
chiffrement réel). **Aucun secret** n'entre dans un DTO de lecture (booléens `Has*` uniquement) ni dans un log
(patron `PaAccount`).

### 4. Config en BASE SYSTÈME (instance), portée par un module INSTANCE-level — PAS Supervision

La config est une **ligne singleton d'instance** en **base système**, via `ISystemConnectionFactory`
(précédent `PostgresFleetStore`). Le **Host ne porte pas de migrations** → la table est portée par un
**module**. Ce module est **instance-level**, **pas `Supervision`** (tenant-scopé, lecture seule cross-tenant ;
ses migrations tourneraient aussi dans chaque base tenant, créant une table de config vide par tenant —
absurde). Cible : **`FleetSupervision`** (déjà base système, déjà en écriture, déjà émetteur d'emails
d'instance) **ou** un module dédié `InstanceSettings`. Le module ne stocke/retourne que du **ciphertext** ; le
**Host** garde le monopole chiffrement/déchiffrement (précédent `GeneriqueAccountResolver`).

### 5. Chiffrement + transport + token provider CÔTÉ HOST

Chiffrement (au save), déchiffrement (au send) et transport provider-aware vivent **côté Host** (composition
root — seul endroit, avec TenantSettings, autorisé à consommer `ISecretProtector`, qui est dans
`TenantSettings.Application`, pas Contracts — CLAUDE.md n°6/14). Précédent exact : les résolveurs de compte PA
du Host. Le **handler de save** (`SaveInstanceEmailConfig`, MediatR) **chiffre** puis persiste le ciphertext ;
le clair ne quitte jamais le handler. **Préservation à l'écriture** : un champ secret **vide** au save
**conserve** le ciphertext existant (lit-puis-conserve), sinon un ré-enregistrement de la page (secrets
masqués `•••` non re-saisis) écraserait les secrets par du vide.

### 6. Précédence de config : ligne DB `enabled` = AUTORITAIRE ; `appsettings` = repli bootstrap

Quand une ligne de config DB existe et est **`enabled`**, elle est **autoritaire** (host/port/tls/auth/
secrets). `SmtpOptions`/`appsettings` ne sert que de **repli au bootstrap** (aucune ligne DB) — jamais un
merge ambigu où un hôte/mot de passe périmé d'`appsettings` l'emporterait en silence. Les **deux branches**
(ligne DB présente / absente) sont testées.

### 7. Console de config (gabarit design-system), permission dédiée `liakont.instance.settings`

Page Host copiée du gabarit **`AdminIntegrations.razor`** (`StratumTabs`/`SectionCard`/`FormField`/
`PermissionGate`/`StratumButton`, inputs `type=password` masqués — **jamais** de form maison ni socle modifié)
: sélecteur de `kind` révélant les champs OAuth, secrets affichés masqués (`•••` si `Has*`), boutons
Enregistrer + **« Envoyer un email de test »**. Gate **`liakont.instance.settings`** (permission **NEUVE**,
action **mutante** d'instance) — **PAS `liakont.supervision`** (documentée « lecture seule, aucune action
mutante » : y accrocher une écriture se contredit). Zéro logique métier dans la page (déléguée au handler —
n°19). **Test bUnit obligatoire** (page Blazor sans test = P1, review n°19).

### 8. Cycle de vie DI : token provider Singleton (respect d'`expires_in`)

`IEmailOAuthTokenProvider` est enregistré **Singleton** (ou adossé à `IMemoryCache`) pour honorer `expires_in`
entre les envois (un provider Scoped rafraîchirait le token à **chaque** envoi → throttling endpoint). Le
transport reste Scoped (lit la config d'instance à chaque envoi, déchiffre en mémoire). **Aucun**
`access_token`/`refresh_token`/`client_secret` en log.

### 9. Portée V1 : SMTP XOAUTH2 ; Graph sendMail = fast-follow

V1 = **SMTP XOAUTH2** (MailKit natif, zéro package). Microsoft Graph `sendMail` (utile si un tenant O365
désactive SMTP AUTH) = **fast-follow** derrière le **même seam** + un ADR + capacité déclarée (nécessiterait
`Microsoft.Graph` + `Microsoft.Identity.Client`). Le prérequis opérateur (activer SMTP AUTH OAuth sur la
mailbox O365) est documenté au déploiement.

## Invariants

- **INV-EMAIL-CFG-01** — Les secrets email (`smtp_password`, `oauth_client_secret`, `oauth_refresh_token`)
  sont **chiffrés au repos** (`ISecretProtector`, purposes dédiés) ; **jamais** en clair en base, dans un DTO
  de lecture (booléens `Has*` seulement) ou un log. `oauth_client_id`/`oauth_tenant_id` sont des non-secrets
  (clair assumé). Chiffrement/déchiffrement **uniquement côté Host**.

- **INV-EMAIL-CFG-02** — La config d'instance vit en **base système** (`ISystemConnectionFactory`, ligne
  singleton), portée par un module **instance-level** (`FleetSupervision`/`InstanceSettings`), **jamais**
  `Supervision` (tenant-scopé, lecture seule). Le **seam vendored `IEmailTransport` n'est pas modifié**
  (impl `services.Replace` au composition root, aucune provenance).

- **INV-EMAIL-CFG-03** — Une ligne DB `enabled` est **autoritaire** ; `appsettings` = repli bootstrap
  uniquement. Un champ secret vide au save **conserve** le secret existant (jamais écrasé par du vide). La
  page de config est gardée par **`liakont.instance.settings`** (écriture), pas `liakont.supervision`.

## Conséquences

**Positif** : config email **paramétrable en console**, secrets **chiffrés** (patron `PaAccount`), support
**Gmail/O365** sans **aucun package neuf** (XOAUTH2 natif MailKit) ; seam vendored et socle **intacts**
(aucune provenance) ; frontières respectées (module = ciphertext, Host = crypto/transport) ; précédence de
config **explicite** (pas de faux-vert de merge) ; permission d'écriture **honnête** (pas de détournement de
la permission read-only Supervision).

**À la charge du(des) lot(s) d'implémentation** : `IEmailOAuthTokenProvider` + `HttpEmailOAuthTokenProvider`
(Host, HttpClient, Singleton) ; `SmtpEmailTransport` provider-aware (branche l'auth sur le kind, config depuis
la DB) ; `EmailSecretPurposes` (Host, purposes DP dédiés) ; `IInstanceEmailConfigStore` +
`PostgresInstanceEmailConfigStore` + **migration** (module instance-level, base système, DTO ciphertext/`Has*`)
; `SaveInstanceEmailConfig` (command + handler MediatR, chiffre + **préserve** les secrets vides) ; page
`EmailSettings.razor` (gabarit `AdminIntegrations`, gate `liakont.instance.settings`) + entrée nav + la
**permission neuve** (const + rôle Keycloak) ; enregistrements DI (`AppBootstrap`) ; amendement/ADR de
supersession sur ADR-0018. **Tests** : bUnit page (kind révèle les champs OAuth, secrets masqués, gate,
délégation MediatR, bouton test) ; transport provider-aware (basic vs XOAUTH2, no-op non-bloquant si non
configuré) ; `HttpEmailOAuthTokenProvider` (bonne requête par endpoint, parse, échec propagé, aucun secret en
log) ; purposes/handler (isolation crypto, ciphertext-only persisté, champ vide ne remplace pas, DTO `Has*`) ;
intégration Testcontainers (round-trip base système, colonnes secrètes = ciphertext, upsert singleton) ;
boundary (MailKit/`ISecretProtector` ne fuient dans aucun module métier ; socle Notification non modifié).

**Limite** : V1 = SMTP XOAUTH2 uniquement (Graph = fast-follow) ; consentement OAuth initial = concern
déploiement (le code ne fait que le refresh) ; config **unique d'instance** (l'override SMTP par tenant de la
livraison Factur-X, `GeneriqueAccountResolver`, n'est **pas** fusionné ici).

### Points NON TRANCHÉS (défaut défendable pris, l'owner tranche, jamais inventé)

| # | Point | Défaut défendable PRIS | Owner |
|---|-------|------------------------|-------|
| D1 | Home du store d'instance (`FleetSupervision` déjà instance-level/écriture/emails vs module dédié `InstanceSettings` — éventuellement mutualisé avec le référentiel pays d'ADR-0038). | **`FleetSupervision`** (moins de churn : déjà base système, déjà en écriture, déjà émetteur d'emails). Alternative : module dédié `InstanceSettings` (mutualisable avec `Reference` d'ADR-0038 si on veut un seul home « config/référentiel d'instance »). | Karl + implémentation |
| D2 | Provider O365 : SMTP XOAUTH2 vs Graph `sendMail` (Microsoft désactive SMTP AUTH par défaut sur certains tenants). | **V1 = SMTP XOAUTH2** (zéro package) ; Graph = fast-follow (ADR + `Microsoft.Graph`/MSAL + capacité). Prérequis opérateur documenté. | Karl |
| D3 | Obtention initiale du `refresh_token` (consentement interactif). | **Hors code** (flux one-time documenté ; opérateur colle le token). Assistant in-app (device-code) = fast-follow + packages. | — |
| D4 | Permission : nouvelle `liakont.instance.settings` vs réutiliser `liakont.supervision`. | **Nouvelle `liakont.instance.settings`** (écriture assumée ; churn RBAC/Keycloak minime en build). | Karl |

## Note d'implémentation (2026-07-01)

Écarts et décisions prises à l'implémentation (défauts défendables de l'ADR appliqués, plus quelques
précisions dictées par le code réel) :

- **D1 → FleetSupervision** (défaut pris). Le store `IInstanceEmailConfigStore` + `InstanceEmailConfig`
  (ciphertext-only) vivent dans **`FleetSupervision.Application`** (miroir de `IFleetInstanceStore`, PAS
  Contracts : consommé au sein du module + Host), l'impl `PostgresInstanceEmailConfigStore` +
  migration `V003__create_instance_email_config.sql` dans `Infrastructure`. Ligne singleton (PK + CHECK sur
  `singleton_id = true`). Le module ne référence JAMAIS `ISecretProtector` (frontière n°6/14).
- **Orchestration = service console Host, pas un handler MediatR.** L'ADR §5/§7 nommait un handler MediatR
  `SaveInstanceEmailConfig` ; mais l'assembly **Host n'est scanné par AUCUN `AddMediatR`** (chaque module
  fait le sien) — bootstrapper MediatR pour le Host juste pour ce cas aurait été du sur-engineering. On suit
  le patron Host établi : un **service console** `IInstanceEmailConfigService` (précédent exact
  `GeneriqueAccountResolver` = service Host qui déchiffre via `ISecretProtector`) orchestre
  chiffrement + store + envoi de test. L'invariant « crypto au Host » (§5, INV-EMAIL-CFG-01) est intégralement
  préservé.
- **Nav sous Supervision** (conformément à la demande recette « en Supervision »). L'entrée
  « Configuration email » (`/email-instance`, hors de l'espace `/supervision/{tenantId}`) est rangée dans
  l'aire opérateur d'instance, gardée par la **présence** de `liakont.supervision` pour la visibilité et par
  la permission neuve **`liakont.instance.settings`** pour l'usage ; le rôle `superviseur` reçoit les deux
  (matrice §3 de `identity-permissions-liakont.md` mise à jour — la nouvelle permission est **sourcée**, pas
  inventée). `SensitivePermissions` reste `{actions, settings}` (la sensibilité a une source autoritative
  ADR-0017 : on n'y ajoute rien sans source).
- **`SmtpOptions` (appsettings) INCHANGÉ** : le multi-provider vit UNIQUEMENT dans la config DB ; le repli
  bootstrap reste SMTP basic (ADR-0018). `EmailDocumentDeliveryChannel` (livraison Factur-X par email, override
  SMTP par tenant) n'est pas touché (hors périmètre, §Limite).
- **Token provider sans verrou** : `HttpEmailOAuthTokenProvider` (Singleton, `IHttpClientFactory`) met le
  jeton en cache par `(kind, client_id, refresh_token)` haché ; la rare course de premiers appels concurrents
  est acceptée (précédent `SuperPdpTokenProvider`), évitant un `SemaphoreSlim` disposable. `scope` omis : le
  rafraîchissement réutilise le consentement d'origine (Google et Microsoft).
- **Correctif de référence** : MailKit 4.17 est en `Directory.Packages.props:69-70` (l'ADR citait à tort
  `:65-66` = NetTopologySuite).
- **Tests** : unitaires (résolution de config transport — précédence DB/appsettings, kind, purpose ;
  service — chiffrement/lit-puis-conserve/`Has*`/envoi de test ; token provider — endpoint par fournisseur,
  parsing, échec, cache, aucun secret en log ; page bUnit — gate, save, test, révélation OAuth, masquage) et
  intégration Testcontainers (round-trip ciphertext + singleton). `verify-fast` + `run-tests` verts.

## Alternatives rejetées

- **Config SMTP mono-provider en `appsettings` (statu quo ADR-0018)** : pas de console, pas d'OAuth, secret
  hors DB non chiffré géré manuellement. **Rejetée** — config chiffrée en DB, multi-provider, console.
- **Tirer Google API/Microsoft Graph/MSAL pour l'ENVOI** : SDK lourds là où MailKit fait XOAUTH2 nativement.
  **Rejetée** — `SaslMechanismOAuth2` + `IEmailOAuthTokenProvider` (HttpClient), zéro package (Graph =
  fast-follow assumé).
- **Store de config dans le module `Supervision`** : Supervision est tenant-scopé, lecture seule cross-tenant ;
  un store d'écriture cross-instance y dilue l'invariant et créerait une table vide par tenant. **Rejetée** —
  module instance-level (`FleetSupervision`/`InstanceSettings`).
- **Créer un `IEmailSender` concurrent** : dédoublerait le seam vendored `IEmailTransport`. **Rejetée** —
  multi-provider derrière le seam existant, `services.Replace` (socle intact, aucune provenance).
- **Gate `liakont.supervision`** : permission « lecture seule, aucune action mutante » ; une écriture s'y
  contredit. **Rejetée** — `liakont.instance.settings`.
- **`updated_by`/save « Protect-or-null » naïf** : écraserait les secrets par du vide au ré-enregistrement
  (champs masqués non re-saisis). **Rejetée** — lit-puis-conserve (champ vide = garder l'existant).
- **Colonne `encrypted_oauth_client_id`** : `client_id` n'est pas un secret ; une colonne `encrypted_` sans
  purpose de chiffrement est trompeuse. **Rejetée** — `oauth_client_id` en clair (comme `oauth_tenant_id`).

## Références

- Demande : `tasks/bugs-recette-encheres-b2c.md` (« Config d'envoi d'emails en Supervision »). Plan :
  `tasks/plan-config-email-instance.md`. ADR **amendé** : `docs/adr/ADR-0018-transport-smtp-mailkit.md`.
- Sources code : `Modules/Notification/Contracts/IEmailTransport.cs` (seam vendored) ;
  `Host/Liakont.Host/Notifications/SmtpEmailTransport.cs` + `SmtpOptions.cs` ; `Startup/AppBootstrap.cs:197`
  (`services.Replace`) ; `Modules/Supervision/Infrastructure/AlertEmailNotifier.cs`,
  `Modules/FleetSupervision/Infrastructure/EmailFleetUpdateNotificationSender.cs` +
  `PostgresFleetStore.cs` (base système) ; `Modules/TenantSettings/Application/ISecretProtector.cs` +
  `PaAccountSecretPurposes.cs` + `Infrastructure/DataProtectionSecretProtector.cs` +
  `AddPaAccountHandler.cs` + `Host/.../GeneriqueAccountResolver.cs` (déchiffrement Host) ;
  `Common/Infrastructure/Database/ISystemConnectionFactory.cs` ; `Directory.Packages.props:65-66` (MailKit
  4.17, `SaslMechanismOAuth2` natif) ; `Modules/Notification/Web/Pages/AdminIntegrations.razor` (gabarit
  form) ; `Host/Security/LiakontPermissions.cs`.
- CLAUDE.md : n°6/14 (frontières ; `ISecretProtector` consommé Host/TenantSettings), n°8 (capacité/kind, pas
  `if (pa is X)`), n°9 (Supervision = cross-tenant lecture seule), n°10/18 (secrets chiffrés = P1),
  n°11/20 (socle non modifié → pas de provenance), n°19 (page Blazor sans test = P1).
