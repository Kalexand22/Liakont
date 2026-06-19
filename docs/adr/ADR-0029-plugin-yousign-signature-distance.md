# ADR-0029 — Plug-in de signature à distance Yousign : provider server-side + webhook routé/durable/idempotent + secrets par tenant + rapatriement WORM (consomme `ISignatureProvider`)

- **Statut** : Proposé (2026-06-16).
- **Date** : 2026-06-16
- **Nature** : cet ADR **précède** le chantier d'implémentation (plug-in Yousign non démarré, **aucun code** ; le
  module `Liakont.Modules.Signature` lui-même est livré par SIG03, le plug-in par SIG07). Les sections **Décision**
  et **Invariants** sont **normatives** : elles décrivent la **cible**, pas l'état du code. Aucun invariant n'est
  garanti tant qu'il n'est pas livré **et** prouvé par test. Cet ADR est une **ADR-fille d'ADR-0022 §6** (frontières
  de la généricité) et une **sœur d'ADR-0024/0025/0026/0027/0028** : il tranche une **frontière de comportement** (un
  plug-in de signature à distance piloté par l'abstraction à capacités d'ADR-0027) ; il ne tranche **aucun point
  fiscal ni juridique** — la signature électronique **n'est pas requise** pour l'acceptation d'une auto-facture
  (F17 §1.1, sourcé CGI 289 I-2 / eIDAS) et **aucun niveau eIDAS n'est imposé** : c'est un **paramétrage tenant**.
- **Numérotation** : ADR-**0029**. Plan d'ADR-filles du lot signature (F17 §9) : 0027 (abstraction
  `ISignatureProvider`), 0028 (module générique `DocumentApproval`), **0029** (ce plug-in Yousign), 0030 (client soft
  Wacom). Les amendements (F15 §1.9, ADR-0024 journal) sont gravés dans le même item (SIG02). *(Note : deux fichiers
  `ADR-0023-*` coexistent — câblage agent et génération Factur-X — collision de numéro assumée « numéro libre toutes
  branches actives confondues » ; sans incidence ici.)*
- **Contexte décisionnel** : `docs/conception/F17-Signature-Validation-Document.md` §5 (plug-in Yousign), §8
  (secrets + anti-SSRF), §9 (plan d'ADR), §10 (points ouverts — défauts défendables), §11 (garde-fous P1) ;
  `docs/adr/ADR-0027-abstraction-signature-capacites.md` (`ISignatureProvider`/`SignatureProviderCapabilities`/
  `SignatureProviderAccount`/`ISignatureProviderFactory` consommés) ; `docs/adr/ADR-0028-workflow-validation-document-generique.md`
  (réconciliation tardive idempotente, arête WORM job de drain → `Archive.Contracts`) ; `docs/adr/ADR-0006-mecanique-jobs-multi-tenant.md`
  (SOL06 : `ITenantJobRunner`) ; patrons réels imités **exactement** : `src/Modules/TenantSettings/Domain/Entities/PaAccount.cs`
  + `src/Modules/TenantSettings/Infrastructure/DataProtectionSecretProtector.cs` (secrets chiffrés par tenant) ;
  `src/Host/Liakont.Host/FleetApi/FleetApiKeyValidator.cs` (`CryptographicOperations.FixedTimeEquals`) ;
  `src/Common/Abstractions/MultiTenancy/ICompanyTenantLookup.cs` (catalogue système handle → tenant) ;
  `src/Modules/Notification/Infrastructure/Handlers/Commands/TestFireWebhookHandler.cs` (re-vérif
  `subscription.CompanyId == request.CompanyId`) ; `src/Modules/Archive/Contracts/IArchiveService.cs`.

## Contexte

Le premier volet de signature à distance est **Yousign** (API REST publique v3). Le plug-in doit s'intégrer
**exclusivement** par l'abstraction d'ADR-0027 (`ISignatureProvider`, comportement piloté par `Capabilities`,
**aucun `if (provider is Yousign)`**) et alimenter le workflow générique d'ADR-0028 (`DocumentApproval`) **sans**
jamais référencer un autre module métier (CLAUDE.md n°6).

Deux difficultés décisives portent sur le **webhook** de complétion :

1. **Le secret HMAC est par tenant** — on ne peut donc pas vérifier la signature du webhook sans d'abord savoir
   **quel** tenant ; or déterminer le tenant **depuis le corps** serait une **requête cross-tenant interdite**
   (CLAUDE.md n°9, blueprint §6).
2. **Le traitement (rapatriement preuve + documents) est lent** — le faire inline dépasse la deadline du webhook,
   mais « répondre 2xx puis traiter » **perdrait** l'événement sur un crash.

S'y ajoutent : la **frontière** plug-in → autre module vendored (ne pas réutiliser un service `Domain` d'un autre
module), la **comparaison HMAC à temps constant**, l'**idempotence** des rejeux, et l'**anti-SSRF** sur l'URL
d'appel sortant (qui porte la clé API). La question tranchée ici : **comment brancher Yousign en respectant ces
contraintes, sans aucune fuite cross-tenant ni perte d'événement ?**

## Décision

### 1. Provider server-side piloté par capacités (ADR-0027), niveaux **DÉCLARÉS**

Plug-in **server-side** (.NET 10) appelant l'**API REST Yousign Public v3** (sandbox / prod sélectionnés par
`Environment`, voir §6). Cycle nominal : `draft` → **upload multipart binaire** du document → `signers`/`fields` →
(pour AES/QES) **pré-vérification d'identité** → `activate` → **webhook** de complétion. Le provider expose une
`SignatureProviderCapabilities` (ADR-0027) :

- `Mode = Remote` ; `CompletionTransport = Webhook | Polling` (webhook primaire + **polling de réconciliation** de
  secours via `GetSignatureStatusAsync`, axe orthogonal au `Mode` — ADR-0027 §2) ;
- `SupportedLevels` = l'**ensemble RÉELLEMENT activé** sur le compte (jamais un max ordonné ; ADR-0027) — une
  capacité **non vérifiée** (ex. QES hors offre) **n'est pas déclarée** et un appel la demandant renvoie le résultat
  **typé `NotSupported`**, **jamais** une exception ni un blocage produit (DÉFAUT DÉFENDABLE F17 §10 #6 : on déclare
  au niveau **réellement vérifié en sandbox** ; les niveaux/coût/limites de l'offre souscrite sont une **activation
  au déploiement**, jamais supposés).

**Frontière P1 (CLAUDE.md n°6) :** le plug-in ne référence **que** `Signature.Contracts` + Common
(NetArchTest) ; **aucun type HTTP / payload propre à Yousign ne traverse `ISignatureProvider`** (le client HTTP et le
modèle Yousign vivent **dans** le plug-in).

### 2. Webhook — routage par **handle de tenant opaque** AVANT vérification (aucun lookup métier pré-scope)

L'URL de webhook porte un **identifiant OPAQUE et non devinable** : `/webhooks/signature/yousign/{opaqueRef}`. La
couche de routage globale **ne résout QU'UN handle de tenant** (`{opaqueRef}` → `tenant`, **pur aiguillage, AUCUNE
donnée métier**) : elle **n'accède pas** au `SignatureProviderAccount` (le résoudre pré-scope serait justement un
**lookup métier cross-tenant**, interdit). Séquence stricte :

```
{opaqueRef} → handle de tenant → ouverture du scope tenant
            → chargement du SignatureProviderAccount + secret HMAC depuis la base DE CE tenant
            → vérification HMAC
```

L'`opaqueRef` **n'est pas un secret** (le HMAC reste exigé) ; il route **sans aucun scan ni lookup métier
cross-tenant**. Le registre `{opaqueRef}` → `tenant` est un **catalogue système d'infra** (modèle
`ICompanyTenantLookup`, contrainte `UNIQUE` sur l'`opaqueRef`), **hors requête métier** — un aiguillage d'infra,
**pas** une vue cross-tenant interdite.

### 3. HMAC vérifié **en interne**, à temps constant, sur le **raw body** (aucun Domain vendored réutilisé)

- Vérification **HMAC-SHA256 sur le RAW body** (en-tête `X-Yousign-Signature-256`).
- **NE PAS réutiliser `WebhookSignature.Compute`** : il vit dans `src/Modules/Notification/Domain/Services/WebhookSignature.cs`
  (couche **Domain d'un autre module**) — un plug-in qui le référence violerait CLAUDE.md n°6 **et** atteindrait un
  Domain vendored. **Décision : le plug-in calcule son HMAC en interne** avec `System.Security.Cryptography`, ou via
  un helper neutre dans un namespace **`Liakont.*` non-vendored EXPLICITE** (ex. `Liakont.Common.Crypto`), **jamais
  mêlé au code `Stratum.*` du même assembly** (`Liakont.Common` héberge déjà du `Stratum.*` — le helper doit être
  identifiable hors socle).
- **Comparaison à temps constant** : `CryptographicOperations.FixedTimeEquals` sur les **octets** du HMAC, **jamais**
  `string.Equals` sur l'hex (patron réel : `src/Host/Liakont.Host/FleetApi/FleetApiKeyValidator.cs`).
- **Aucune modification du socle vendored `Stratum.*`** (CLAUDE.md n°11). Si une primitive devait un jour être
  ajoutée au socle, elle serait consignée dans `docs/architecture/provenance-socle-stratum.md` — ce qui n'est **pas**
  le cas ici.

### 4. Durabilité — inbox persistée **AVANT 2xx**, traitement **asynchrone** par job

Le handler webhook fait le **strict minimum SYNCHRONE** : (a) router + ouvrir le scope tenant (§2), (b) vérifier le
HMAC (§3), (c) **persister l'événement brut — authentifié et idempotent — dans une FILE DURABLE**
(`signature_webhook_inbox`, **tenant-scopée**) **AVANT** de répondre **2xx (< 1 s)**. Puis le traitement lourd
(rapatriement preuve + documents, transition `DocumentApproval`) est **asynchrone** : un job réutilisant
`TenantJobRunner` (SOL06) **draine** l'inbox.

- ⚠️ **Ni** traitement inline (trop lent → dépasse la deadline), **ni** « 2xx d'abord, traiter ensuite » (un crash
  **perdrait** l'événement). Persistance **avant** 2xx, drain **après**.
- **Idempotence par clé `(company_id, provider_type, event_id)`** (jamais `event_id` seul : deux tenants/providers
  peuvent partager un `event_id`), **à l'inbox ET au traitement**.
- **Backoff exponentiel + jitter sur 429** sur les appels sortants Yousign.
- La réconciliation tardive depuis un état terminal suit ADR-0028 §7 : événement déjà traité **ignoré** (idempotence)
  ou **journalisé comme tentative rejetée** sans muter le terminal.

### 5. Rapatriement WORM par le **JOB DE DRAIN**, via `Archive.Contracts` (jamais le plug-in, jamais `Archive.Domain`)

Sur `signature_request.done` (drainé depuis l'inbox, en asynchrone) : **rapatriement systématique de la preuve +
des documents signés dans le coffre WORM Liakont** — c'est le **job de drain** qui écrit, **via `Archive.Contracts`
(`IArchiveService`)**, **jamais le plug-in Yousign** (qui ne référence que `Signature.Contracts` + Common, §1) ni
`Archive.Domain`/un backend concret (CLAUDE.md n°6, indépendance backend). Rapatriement **même si** Yousign archive
de son côté. C'est l'arête WORM gravée par ADR-0028 §9.

### 6. Secrets par tenant chiffrés + URL en **allowlist** (anti-SSRF)

- **Secrets (CLAUDE.md n°10) :** clé API + secret webhook = **secrets PAR TENANT, chiffrés en base, jamais en
  clair**, sur le patron `PaAccount` / `DataProtectionSecretProtector` (module `TenantSettings`). Entité
  `SignatureProviderAccount` (ADR-0027) : `CompanyId, ProviderType, Environment, AccountIdentifiers` (non secrets :
  workspace, niveau défaut), `EncryptedApiKey?`, `EncryptedWebhookSecret?`. Purpose DataProtection **dédié et
  versionné** (`Liakont.Signature.ProviderAccount.ApiKey.v1`). `Authorization Bearer` et HMAC construits **en
  mémoire, jamais journalisés**. Rotation sans redéploiement.
- ⚠️ **L'URL de base N'EST PAS un champ tenant libre (anti-SSRF) :** elle est **dérivée d'une allowlist d'URI HTTPS
  EXACTES par provider × `Environment`** — Yousign sandbox `https://api-sandbox.yousign.app/v3` / prod
  `https://api.yousign.app/v3` (origines `https://…` complètes, définies au plug-in/environnement). Le tenant ne
  choisit qu'**entre des `Environment` CONNUS** (jamais une adresse, jamais un host/path nu). **Renforcement
  (correction P2) :** l'allowlist liste des **origines `https://` exactes** (schéma + host + port) et le plug-in
  **REJETTE** (a) tout schéma **non-HTTPS** (`http`, `file`, … → refus), (b) toute origine hors liste, et (c) **toute
  redirection (3xx) vers une cible non listée** (`AllowAutoRedirect = false`, ré-validation de chaque saut contre
  l'allowlist). Sinon un admin tenant — ou un `http://`/une redirection — ferait émettre des appels **authentifiés
  (porteurs de la clé API Bearer)** vers une adresse interne/arbitraire = **SSRF + fuite de la clé API**. Test : une
  URL arbitraire, un `http://`, et une redirection vers une cible non listée sont **tous refusés**.
- **Aucune donnée client dans le code (CLAUDE.md n°7)** : tout en `deployments/<client>/` ou exemples fictifs dans
  `config/exemples/`.

### 7. Portée : structure et comportement, **aucun code, aucune décision fiscale ni juridique**

Cet ADR **n'écrit aucun code** (plug-in livré par SIG07). Il ne fixe **aucun** niveau eIDAS par défaut produit
(paramétrage tenant — ADR-0027 §7), n'impose aucune signature, et ne tranche **aucun point juridique** (les points
ouverts F17 §10 restent des **défauts paramétrables**, jamais des gates). **Les ADR de package** éventuels (client
HTTP Yousign) restent à inventorier avant dev (Post-Dev Checklist), à la charge de SIG07.

## Invariants

- **INV-YOUSIGN-1** — Le plug-in est piloté **exclusivement** par `Capabilities` (ADR-0027) ; **aucun
  `if (provider is Yousign)`** ailleurs dans le produit. Une capacité/un niveau non déclaré → résultat typé
  `NotSupported` (message opérateur FR), **jamais** une exception ni un blocage produit (test : QES hors offre →
  `NotSupported`).
- **INV-YOUSIGN-2** — **Frontière P1** : le plug-in ne référence que `Signature.Contracts` + Common (NetArchTest) ;
  **aucun type HTTP ne traverse `ISignatureProvider`** ; en particulier, il **ne référence pas**
  `Notification.Domain` (`WebhookSignature`) ni aucun autre module métier.
- **INV-YOUSIGN-3** — Webhook : routage par **handle de tenant opaque** (catalogue système `ICompanyTenantLookup`,
  `UNIQUE`) → **scope tenant** → secret HMAC **du tenant** → **HMAC interne** (`Liakont.*` non-vendored,
  `FixedTimeEquals` sur le **raw body**). **Aucun lookup métier cross-tenant pré-scope** (test) ; une signature
  **falsifiée est rejetée AVANT tout traitement** (test).
- **INV-YOUSIGN-4** — L'événement brut est **persisté dans `signature_webhook_inbox` (tenant-scopée) AVANT** la
  réponse **2xx** ; le traitement lourd est **asynchrone** (drain par `TenantJobRunner`). Un **crash après 2xx ne
  perd pas l'événement** (test).
- **INV-YOUSIGN-5** — **Idempotence par `(company_id, provider_type, event_id)`** (jamais `event_id` seul), à
  l'inbox **et** au traitement (test : rejeu d'un même événement = sans effet). **Backoff + jitter sur 429**.
- **INV-YOUSIGN-6** — Le **rapatriement WORM** (preuve + documents) est fait par le **job de drain** via
  `Archive.Contracts` (`IArchiveService`), **jamais** par le plug-in, **jamais** via `Archive.Domain`/un backend
  concret (NetArchTest : le job `NotHaveDependencyOnAny("Liakont.Modules.Archive.Domain", "…Stores.*")`).
- **INV-YOUSIGN-7** — Secrets **par tenant chiffrés** (patron `DataProtectionSecretProtector`, purpose versionné),
  **jamais en clair ni journalisés** (clé API / secret webhook / `Authorization Bearer`). L'URL de base est
  **dérivée d'une allowlist d'origines `https://` EXACTES par `Environment`** (anti-SSRF) — **jamais** un champ tenant
  libre ; le plug-in **rejette** le non-HTTPS, les origines hors liste et les redirections (3xx) vers une cible non
  listée (`AllowAutoRedirect = false`). Tests : une URL arbitraire, un `http://` et une redirection hors allowlist
  sont **tous refusés**.
- **INV-YOUSIGN-8** — **Aucune modification du socle vendored `Stratum.*`** (vérifié par `socle-baseline` ; toute
  primitive ajoutée serait consignée en provenance).

## Conséquences

**Positif** : Yousign est branché **sans aucun `if (provider is …)`** ni flag produit (capacités seules) ; le
webhook ne crée **aucune fuite cross-tenant** (routage handle-opaque sans lookup métier pré-scope) et **ne perd
aucun événement** (inbox durable avant 2xx + drain idempotent) ; la frontière plug-in (Contracts seuls, HMAC interne,
WORM via le job de drain) est **prouvable par NetArchTest** ; l'anti-SSRF ferme une fuite concrète de la clé API.
On **réutilise** `TenantJobRunner`, `ICompanyTenantLookup`, le patron `DataProtectionSecretProtector`,
`FixedTimeEquals` et `Archive.Contracts` — **aucun mécanisme transverse nouveau, aucun code `Stratum.*` vendored
modifié.**

**À la charge du(des) lot(s) d'implémentation** (SIG07) : plug-in Yousign (client HTTP v3 sandbox/prod, cycle
draft→upload multipart→signers→activate→webhook) ; route webhook + catalogue `{opaqueRef}` → tenant ; helper HMAC
`Liakont.*` non-vendored + `FixedTimeEquals` ; migration `signature_webhook_inbox` (tenant-scopée, idempotence
`(company_id, provider_type, event_id)`) ; job de drain (`TenantJobRunner`) → rapatriement WORM via
`Archive.Contracts` ; `SignatureProviderAccount` chiffré (purpose versionné) ; allowlist d'URL par `Environment` ;
backoff+jitter 429 ; ADR de package si un client HTTP tiers est ajouté ; tests : HMAC falsifié rejeté avant
traitement, crash-après-2xx ne perd pas l'événement, idempotence du rejeu, frontière NetArchTest (Contracts seuls +
arête WORM), URL arbitraire refusée, tenant-scoping ≥ 2 bases.

**Limite** : cet ADR ne grave **ni** le client soft Wacom (ADR-0030), **ni** le module `Signature` lui-même
(ADR-0027 / SIG03), **ni** le workflow `DocumentApproval` (ADR-0028 / SIG04), **ni** un niveau eIDAS par défaut
(paramétrage tenant).

### Points NON TRANCHÉS (F17 §10 — défaut défendable pris, le client tranche au déploiement, jamais inventé)

| # | Point | Défaut défendable PRIS | Owner |
|---|---|---|---|
| 6 | Niveaux / offre / limites / retry réels de l'offre Yousign souscrite | capacités **DÉCLARÉES** au niveau **réellement vérifié en sandbox** ; AES/QES = **activation au déploiement**, jamais supposés (une capacité non vérifiée → `NotSupported`) | investigation tech (coût → Karl) |
| 1 | Niveau eIDAS exigé par purpose | **paramétrage tenant** (la Règle de gate ADR-0028 §5 vérifie le niveau attaché) ; jamais un défaut produit ni une obligation codée | tenant + son EC |

Aucun de ces points ne stalle le dev : ce sont des **défauts paramétrables**, pas des gates (F17 §10).

## Alternatives rejetées

- **Déterminer le tenant depuis le corps du webhook** (pour charger le secret) : **requête cross-tenant interdite**
  (CLAUDE.md n°9). **Rejetée** — handle de tenant opaque dans l'URL, routage d'infra sans lookup métier pré-scope.
- **Résoudre le `SignatureProviderAccount` dans la couche de routage globale** (pré-scope) : c'est un **lookup
  métier cross-tenant**. **Rejetée** — la couche globale ne résout **que** `{opaqueRef}` → tenant, le compte est
  chargé **après** ouverture du scope.
- **Réutiliser `WebhookSignature.Compute`** (`Notification.Domain`) : viole la frontière plug-in → Domain d'un autre
  module vendored (CLAUDE.md n°6/11). **Rejetée** — HMAC interne `Liakont.*` non-vendored.
- **Comparer le HMAC via `string.Equals` sur l'hex** : non timing-safe. **Rejetée** — `FixedTimeEquals` sur les
  octets.
- **Traiter le webhook inline** (download + WORM dans le handler) : dépasse la deadline. **Rejetée.**
- **Répondre 2xx puis traiter** (sans persistance préalable) : un crash perd l'événement. **Rejetée** — inbox
  durable persistée **avant** 2xx + drain asynchrone.
- **Idempotence par `event_id` seul** : deux tenants/providers peuvent partager un `event_id`. **Rejetée** — clé
  `(company_id, provider_type, event_id)`.
- **URL de base en champ tenant libre** : SSRF + fuite de la clé API. **Rejetée** — allowlist par `Environment`.
- **Allowlist en host/path nu (sans schéma) ou suivant les redirections** : un `http://` ou une redirection 3xx vers
  une cible non listée contournerait l'anti-SSRF en emportant la clé API Bearer. **Rejetée** — origines `https://`
  **exactes**, non-HTTPS refusé, `AllowAutoRedirect = false` + ré-validation de chaque saut.
- **Rapatriement WORM par le plug-in ou via `Archive.Domain`** : viole la frontière (CLAUDE.md n°6). **Rejetée** —
  job de drain via `Archive.Contracts`.

## Références

- `docs/conception/F17-Signature-Validation-Document.md` §5 (plug-in Yousign), §8 (secrets + anti-SSRF), §9 (plan
  d'ADR), §10 (points ouverts), §11 (garde-fous P1).
- `docs/adr/ADR-0027-abstraction-signature-capacites.md` (`ISignatureProvider`, capacités, `SignatureProviderAccount`,
  `NotSupported`) ; `docs/adr/ADR-0028-workflow-validation-document-generique.md` (réconciliation tardive idempotente,
  arête WORM) ; `docs/adr/ADR-0006-mecanique-jobs-multi-tenant.md` (SOL06 `TenantJobRunner`).
- Patrons réels imités : `src/Modules/TenantSettings/Domain/Entities/PaAccount.cs` +
  `src/Modules/TenantSettings/Infrastructure/DataProtectionSecretProtector.cs` (secrets chiffrés par tenant) ;
  `src/Host/Liakont.Host/FleetApi/FleetApiKeyValidator.cs` (`FixedTimeEquals`) ;
  `src/Common/Abstractions/MultiTenancy/ICompanyTenantLookup.cs` (catalogue handle → tenant) ;
  `src/Modules/Notification/Infrastructure/Handlers/Commands/TestFireWebhookHandler.cs` (re-vérif
  `CompanyId`) ; `src/Modules/Archive/Contracts/IArchiveService.cs`.
- ADR-fille d'**ADR-0022** (frontières) ; sœurs **ADR-0024/0025/0026/0027/0028**. ADR-fille suivante du lot :
  **ADR-0030** (client soft Wacom). eIDAS : règlement UE 910/2014 ; API Yousign Public v3. CGI art. 289 I-2.
