# ADR-0027 — Abstraction de signature électronique enfichable à capacités : `ISignatureProvider` + `SignatureProviderCapabilities` (signature OPTIONNELLE, jamais un gate imposé)

- **Statut** : Proposé (2026-06-16).
- **Date** : 2026-06-16
- **Nature** : cet ADR **précède** le chantier d'implémentation (module `Liakont.Modules.Signature` non démarré,
  **aucun code**). Les sections **Décision** et **Invariants** sont **normatives** : elles décrivent la **cible**,
  pas l'état du code. Aucun invariant n'est garanti tant qu'il n'est pas livré **et** prouvé par test. Cet ADR est
  une **ADR-fille d'ADR-0022 §6** (frontières de la généricité) et une **sœur d'ADR-0024/0025/0026** : il tranche
  une **frontière de comportement** (une abstraction de signature pilotée par capacités, façon `IPaClient`) ; il ne
  tranche **aucun point fiscal ni juridique** — la signature électronique **n'est pas requise** pour l'acceptation
  d'une auto-facture (F17 §1.1, sourcé CGI 289 I-2 / eIDAS) et **aucun niveau eIDAS n'est imposé** : c'est un
  **paramétrage tenant**.
- **Numérotation** : ADR-**0027**. Plan d'ADR-filles du lot signature (F17 §9) : **0027** (cette abstraction),
  **0028** (module générique `DocumentApproval`), **0029** (plug-in Yousign), **0030** (client soft Wacom). Les ADR
  0029/0030 et les amendements (F15 §1.9, ADR-0024 journal) sont gravés séparément (lot SIG, item SIG02). *(Note :
  deux fichiers `ADR-0023-*` coexistent dans `docs/adr/` — câblage agent et génération Factur-X — collision de
  numéro assumée « numéro libre toutes branches actives confondues » ; sans incidence ici.)*
- **Contexte décisionnel** : `docs/conception/F17-Signature-Validation-Document.md` §2 (abstraction à capacités),
  §7 (niveau eIDAS proportionné, jamais obligation), §9 (plan d'ADR), §10 (points ouverts — défauts défendables),
  §11 (garde-fous P1 testables) ; patrons réels imités **exactement** : `src/Modules/Transmission/Contracts/`
  (`IPaClient`, `PaCapabilities`, `PaCapabilityNotSupportedResult`, `IPaClientFactory`, `IPaClientRegistry`) ;
  sélecteur au composition root sur le modèle de l'abstraction IdP
  (`src/Host/Liakont.Host/Security/Abstractions/IIdentityProviderAuthenticator.cs`).

## Contexte

Liakont doit pouvoir brancher des fournisseurs de signature électronique hétérogènes — un service **à distance**
(Yousign, server-side) et un **capteur sur place** (client soft Wacom) — sans que le code produit ne dépende
d'aucun fournisseur concret (CLAUDE.md n°6/8). Les deux fournisseurs diffèrent sur des axes **indépendants** : la
**localisation** de la signature (à distance / sur place), le **mode de complétion** (synchrone / webhook / polling),
les **niveaux eIDAS** réellement licenciés sur le compte (SES/AES/QES), la vérification d'identité, le scellement.

La contrainte produit décisive (F17 §1.1, §7) : **la signature n'est PAS requise** par un texte primaire pour
l'acceptation d'une auto-facture, et **aucun niveau eIDAS n'est imposé**. La signature est une **bonne pratique
probatoire optionnelle**. En coder l'absence comme un blocage produit, ou conditionner un gate « parce que la loi
l'exige », **violerait CLAUDE.md n°2/3**. L'abstraction doit donc rendre la signature **pleinement optionnelle** :
un tenant qui n'active aucun fournisseur fonctionne par défaut en acceptation **enregistrée sans signature**
(`Recorded`, conforme ADR-0024).

La question tranchée ici : **quelle abstraction permet de brancher ces fournisseurs sans `if (provider is X)`, en
modélisant des comportements orthogonaux, et en gardant la signature optionnelle ?**

## Décision

Nouveau module **`Liakont.Modules.Signature`** (pattern Stratum `Contracts/Domain/Application/Infrastructure/Web` +
`MODULE.md`/`INVARIANTS.md`). Le contrat est calqué **exactement** sur le couple
`IPaClient`/`PaCapabilities`/`PaCapabilityNotSupportedResult`/`IPaClientFactory` du module `Transmission`.

### 1. `ISignatureProvider` : un contrat dont la **capacité est la seule source de vérité**

```
interface ISignatureProvider
    SignatureProviderCapabilities Capabilities { get; }   // seule source de vérité du comportement
    RequestSignatureAsync(...)
    GetSignatureStatusAsync(...)
    DownloadProofAsync(...)
    HandleWebhookAsync(...)
```

Le comportement est piloté **exclusivement** par `Capabilities`. **Aucun `if (provider is Yousign)`** nulle part
dans le produit (CLAUDE.md n°8/16). **Frontière P1** : un plug-in ne référence que `Signature.Contracts` + Common
(NetArchTest) ; **aucun type HTTP ne traverse l'interface** — le payload propre au fournisseur vit **dans** le
plug-in.

### 2. `record sealed SignatureProviderCapabilities`

- `ProviderName` (porté dans les messages opérateur français).
- `Mode` — **`[Flags] enum SignatureMode { None = 0, Remote = 1, OnSite = 2 }`**. Décrit la **localisation** de la
  signature. ⚠️ valeurs en **puissances de deux distinctes, `None = 0`** : un `[Flags]` avec `Remote = 0` rendrait
  `HasFlag(Remote)` **toujours vrai** (bug C# classique — interdit).
- `CompletionTransport` — **`[Flags] enum CompletionTransport { None = 0, Synchronous = 1, Webhook = 2, Polling = 4 }`**.
  Décrit **comment** la complétion est signalée. **Axe ORTHOGONAL à `Mode` et COMBINABLE** : un fournisseur peut
  déclarer `Webhook | Polling` (webhook primaire + **polling de réconciliation** en secours), un distant
  *polling-only* `Polling`, un capteur sur place `Synchronous`. `HandleWebhookAsync` est pertinent **ssi le flag
  `Webhook` est positionné** ; le flag `Polling` autorise un job de réconciliation `GetSignatureStatusAsync`.
- `SupportedLevels` — **`[Flags]` sur `SignatureLevel { None = 0, Recorded = 1, SES = 2, AES = 4, QES = 8 }`**.
  C'est l'**ENSEMBLE des niveaux RÉELLEMENT activés** sur le compte, **jamais un maximum ordonné** : un compte peut
  offrir `QES` sans `AES`, ou `SES | QES`. Déduire « AES disponible car niveau ≥ AES » ferait demander un niveau
  **non licencié** (la capacité reste la source de vérité). `Recorded` (acceptation enregistrée **sans** signature,
  **défaut conforme ADR-0024**) est **toujours** implicitement disponible. `Supports(level)` = test
  d'**appartenance** à l'ensemble.
- `SupportsSignerIdentityVerification` — pré-vérification d'identité. **Capacité technique, jamais un gate imposé**
  (§7 F17).
- `SupportsDocumentHashBinding` — scellement (art. 26 d eIDAS).
- `SupportsBiometricCapture`.
- `SupportsBiometricTemplateMatching` — **OPT-IN, `false` par défaut** (bascule RGPD art. 9 — la qualification fine
  relève du DPO du client, tranchée dans ADR-0030 ; ici le flag par défaut `false` est la seule décision).
- `MaxDocumentSizeBytes?` — `null` si le fournisseur ne déclare pas de limite.
- méthodes `Supports(SignatureLevel)` / `Supports(SignatureMode)` **centralisant le test** (modèle `PaCapabilities.SupportsPaymentReport`).

> **`Mode` (localisation) et `CompletionTransport` (transport de complétion) sont ORTHOGONAUX.** Le transport est
> modélisé **explicitement**, **jamais déduit** de la localisation : un fournisseur distant *polling-only* ou un
> capteur sur place asynchrone existent, et déduire le webhook de `Mode` rapporterait un comportement erroné
> (proche de CLAUDE.md n°8). `ValidationInProgress` côté workflow (ADR-0028) se décide sur le **résultat** de
> `RequestSignatureAsync` (`Pending` vs `Completed`), **jamais** sur une égalité d'enum (`Synchronous | Polling`
> satisfait `!= Synchronous` sans être « purement asynchrone »).

### 3. Capacité/niveau absent → résultat **typé** `NotSupported`, jamais une exception

Un appel dont la capacité ou le niveau n'est pas activé renvoie
`SignatureRequestResult.NotSupported(SignatureCapabilityNotSupportedResult)` — **message opérateur FR**
(CLAUDE.md n°12), **typé**, **journalisable**, **JAMAIS une exception ni un blocage produit** (modèle exact de
`PaCapabilityNotSupportedResult.Create` / `PaSendResult.NotSupported`). En particulier, un fournisseur **sans le
flag `Webhook`** dans `CompletionTransport` (ex. capteur Wacom sur place, `Synchronous`) renvoie `NotSupported`
sur `HandleWebhookAsync`.

### 4. `ISignatureProviderFactory` + registre indexé par type + sélecteur au composition root

```
interface ISignatureProviderFactory
    string ProviderType { get; }
    ISignatureProvider Create(SignatureProviderAccount account);   // aucun secret en clair dans le descripteur
```

CHAQUE plug-in fournit sa fabrique et s'enregistre dans le conteneur DI ; un `ISignatureProviderRegistry` les
indexe par `ProviderType` (insensible à la casse) — modèle `IPaClientFactory`/`IPaClientRegistry`. La résolution
des secrets (chiffrés par tenant) est **interne** au plug-in ; `SignatureProviderAccount` n'en porte **aucun** en
clair (détail des secrets : ADR-0029/0030, item SIG02 — hors périmètre de cet ADR).

**Sélecteur au composition root** (`AppBootstrap`), sur le **modèle de l'abstraction IdP**
(`IIdentityProviderAuthenticator`), **avec une différence essentielle : la signature est OPTIONNELLE.** Donc :

- `ValidateConfiguration()` **ne bloque le démarrage QUE pour un fournisseur effectivement CONFIGURÉ mais
  MALFORMÉ** ;
- **l'absence de tout fournisseur n'est JAMAIS une erreur de démarrage** : la capacité reste indisponible, le défaut
  `Recorded` fonctionne. Bloquer la plateforme entière faute de signature configurée serait un **durcissement non
  justifié** (CLAUDE.md n°3).

### 5. Portée : structure et comportement, **aucun code, aucune décision fiscale ni juridique**

Cet ADR **n'écrit aucun code** (livré par SIG03 : Contracts + sélecteur + un fournisseur Fake de parité ; aucun
fournisseur concret). Il **ne fixe aucun niveau eIDAS** par défaut produit (paramétrage tenant — §7 F17), n'impose
aucune signature, et ne tranche **aucun point juridique** (les points ouverts F17 §10 restent des **défauts
paramétrables**, jamais des gates — voir « Points NON TRANCHÉS » ci-dessous).

## Invariants

- **INV-SIGPROV-1** — Le comportement d'un `ISignatureProvider` est piloté **exclusivement** par
  `Capabilities` ; **aucun `if (provider is …)`** dans le produit ni dans un autre plug-in (test : comportement
  obtenu en faisant varier la seule capacité).
- **INV-SIGPROV-2** — `SignatureMode` et `SignatureLevel` ont des valeurs **`[Flags]` distinctes en puissances de
  deux avec `None = 0`** (test : `HasFlag(Remote)` est **faux** pour un fournisseur `OnSite`).
- **INV-SIGPROV-3** — `CompletionTransport` est un **`[Flags]` combinable orthogonal à `Mode`** : `Webhook | Polling`
  coexistent (test) ; `HandleWebhookAsync` n'est pertinent **que** si le flag `Webhook` est positionné, sinon
  `NotSupported` (test).
- **INV-SIGPROV-4** — `SupportedLevels` est un **ensemble explicite** (pas un maximum ordonné) : on ne demande
  **jamais** un niveau non présent dans l'ensemble (test : un compte `SES | QES` ne se voit jamais demander `AES`).
  `Recorded` est **toujours** implicitement disponible.
- **INV-SIGPROV-5** — Une capacité ou un niveau absent renvoie un **résultat typé `NotSupported`** (message
  opérateur FR), **jamais** une exception ni un blocage produit (test : appel sans la capacité → `NotSupported`).
- **INV-SIGPROV-6** — La **signature est OPTIONNELLE** : `ValidateConfiguration()` ne bloque le démarrage **que**
  pour un fournisseur **configuré mais malformé** ; **l'absence de tout fournisseur n'est jamais une erreur**
  (test : un tenant `Recorded` démarre **sans** aucun fournisseur configuré).
- **INV-SIGPROV-7** — `SupportsBiometricTemplateMatching` vaut **`false` par défaut** (la capture brute n'est pas
  gouvernée par ce flag ; il gouverne uniquement le *matching* — bascule RGPD art. 9, détaillée ADR-0030).
- **INV-SIGPROV-8** — **Frontière P1** : un plug-in de signature ne référence que `Signature.Contracts` + Common
  (NetArchTest) ; **aucun type HTTP / payload propre au fournisseur ne traverse `ISignatureProvider`**.

## Conséquences

**Positif** : le produit ajoute un fournisseur de signature **sans aucun `if (provider is …)`** ni flag produit ;
les axes `Mode` / `CompletionTransport` / `SupportedLevels` sont modélisés **explicitement et orthogonalement**, ce
qui évite de rapporter un comportement erroné ; la signature reste **optionnelle** (défaut `Recorded` conforme
ADR-0024) ; le contrat est **strictement** calqué sur `IPaClient`/`PaCapabilities` (charge cognitive minimale,
patrons de test réutilisés). **Aucun code `Stratum.*` vendored n'est modifié.**

**À la charge du(des) lot(s) d'implémentation** (SIG03 et suivants) : `Signature.Contracts`
(`ISignatureProvider`, `SignatureProviderCapabilities`, `SignatureMode`/`CompletionTransport`/`SignatureLevel` en
`[Flags]`, `SignatureRequestResult.NotSupported(SignatureCapabilityNotSupportedResult)` avec libellés FR,
`ISignatureProviderFactory`/`ISignatureProviderRegistry`, `SignatureProviderAccount` sans secret en clair) ; un
fournisseur **Fake** de parité (tests) ; sélecteur `AppBootstrap` avec `ValidateConfiguration()` optionnelle ;
assertions NetArchTest de la frontière du plug-in ; test « tenant `Recorded` démarre sans fournisseur ».

**Limite** : cet ADR ne tranche **ni** les fournisseurs concrets (Yousign = ADR-0029 ; Wacom = ADR-0030, item
SIG02), **ni** le détail des secrets/URL anti-SSRF (ADR-0029/0030), **ni** un niveau eIDAS par défaut (paramétrage
tenant).

### Points NON TRANCHÉS (F17 §10 — défaut défendable pris, le client tranche au déploiement, jamais inventé)

| # | Point | Défaut défendable PRIS | Owner |
|---|---|---|---|
| 1 | Niveau eIDAS par besoin | `Recorded` pour l'acceptation (signature non requise, sourcé) ; mandat **configurable** | tenant + son EC |
| 6 | Niveaux/offre/limites Yousign réellement licenciés | capacités **DÉCLARÉES** au niveau réellement vérifié | investigation tech (coût → Karl) |
| 7 | RGPD biométrie (art. 9 / AIPD / rétention / consentement B2B) | sobre : `SupportsBiometricTemplateMatching = false` | DPO du client au déploiement |

Aucun de ces points ne stalle le dev : ce sont des **défauts paramétrables**, pas des gates (F17 §10).

## Alternatives rejetées

- **Dériver le transport de complétion de `Mode`** (`OnSite` ⇒ synchrone, `Remote` ⇒ webhook) : **faux** pour une
  abstraction générique (un distant *polling-only* ou un capteur asynchrone existent) ; la capacité rapporterait un
  comportement erroné (proche de CLAUDE.md n°8). **Rejetée** — `CompletionTransport` est un axe explicite.
- **`SupportedLevels` comme un maximum ordonné** (`Level ≥ AES`) : ferait demander un niveau **non licencié**.
  **Rejetée** — ensemble explicite, `Supports(level)` = appartenance.
- **`ValidateConfiguration()` qui bloque le démarrage en l'absence de fournisseur** (par symétrie avec l'IdP) :
  **durcissement non justifié** (la signature est optionnelle, CLAUDE.md n°3). **Rejetée.**
- **Lever une exception sur capacité absente** : casse la composition générique et le pilotage par capacités.
  **Rejetée** — résultat typé `NotSupported` (modèle `PaSendResult`).
- **Piloter par `if (provider is Yousign/Wacom)`** : viole CLAUDE.md n°6/8/16. **Rejetée.**

## Références

- `docs/conception/F17-Signature-Validation-Document.md` §2 (abstraction à capacités), §7 (eIDAS proportionné,
  jamais obligation), §9 (plan d'ADR), §10 (points ouverts), §11 (garde-fous P1).
- Patrons réels imités : `src/Modules/Transmission/Contracts/` (`IPaClient`, `PaCapabilities`,
  `PaCapabilityNotSupportedResult`, `IPaClientFactory`, `IPaClientRegistry`, `PaSendResult`) ; sélecteur IdP
  `src/Host/Liakont.Host/Security/Abstractions/IIdentityProviderAuthenticator.cs`.
- ADR-fille d'**ADR-0022** (frontières de la généricité) ; sœurs **ADR-0024** (workflow self-billing — l'acceptation
  `Recorded` par défaut), **ADR-0025**, **ADR-0026**. ADR-fille suivante du lot : **ADR-0028** (module générique
  `DocumentApproval` qui **consomme** cette abstraction). eIDAS : règlement UE 910/2014 art. 3/25/26 ; CGI art. 289 I-2.
