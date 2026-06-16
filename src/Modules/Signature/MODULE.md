# Module Signature

> Abstraction de signature électronique **enfichable à capacités** — F17 §2 ; ADR-0027 ; item SIG03
> (scaffolding). Liakont branche des fournisseurs de signature hétérogènes (un service **à distance** type
> Yousign, un capteur **sur place** type Wacom) sans que le code produit ne dépende d'aucun fournisseur
> concret. SIG03 livre l'**ossature du module** (5 couches) + le contrat `ISignatureProvider` piloté par
> capacités + le registre par type + le sélecteur/validation au composition root + un fournisseur **Fake**
> de parité (tests). Les plug-ins concrets (Yousign = SIG07, Wacom = SIG08), le câblage de la règle de
> gate (SIG06) et la console (SIG10) arrivent ensuite.

## Purpose

Exposer une abstraction `ISignatureProvider` dont le **comportement est piloté exclusivement par les
capacités déclarées** (`SignatureProviderCapabilities`), jamais par un `if (provider is Yousign)`
(CLAUDE.md n°6/8/16). Trois axes **orthogonaux** sont modélisés explicitement : la **localisation**
(`SignatureMode` : Remote / OnSite), le **transport de complétion** (`CompletionTransport` : Synchronous /
Webhook / Polling — combinable) et l'**ensemble des niveaux de preuve activés** (`SignatureLevel` :
Recorded / SES / AES / QES — ensemble d'appartenance, jamais un maximum ordonné). Règles absolues
(ADR-0027 ; CLAUDE.md n°2/3) : la signature **n'est PAS requise** pour l'acceptation d'une auto-facture
(F17 §1.1) et **aucun niveau eIDAS n'est imposé** (paramétrage tenant) ; une capacité absente retourne un
**résultat typé `NotSupported`** (message opérateur FR), **jamais une exception ni un blocage** ; la
signature est **OPTIONNELLE** — l'absence de tout fournisseur n'est jamais une erreur (défaut `Recorded`).

## Boundaries

| Ressource / surface | Accès | Détail |
|---|---|---|
| `ISignatureProvider` (abstraction) | **abstraction publique** | Pilotée par `Capabilities`. **Aucun type HTTP ne la traverse** (INV-SIGPROV-8) : le payload propre au fournisseur vit dans le plug-in. |
| `SignatureProviderCapabilities` | **surface publique** | Seule source de vérité du comportement (modèle `PaCapabilities`). |
| `ISignatureProviderFactory` / `ISignatureProviderRegistry` | **abstraction publique** | Résolution par **clé de type** uniquement (jamais `if (type == …)`). Type demandé inconnu → lève (config) ; registre **vide = valide** (signature optionnelle). |
| Plug-ins concrets (`Liakont.SignatureProviders.*`, client `clients/OnSiteSignature`) | **interdit** dans ce module | Un plug-in ne référence que `Signature.Contracts` + Common (NetArchTest, INV-SIGPROV-8) — frontière verrouillée dès SIG03, exercée par les plug-ins SIG07/SIG08. |
| Autre module métier / socle vendored | **interdit** | L'abstraction est **BCL-only** : aucune référence inter-module (`module-rules §3`, CLAUDE.md n°14), aucun `Stratum.*` (CLAUDE.md n°11). |
| `Archive.Contracts` (rapatriement WORM de la preuve) | **via Contracts au niveau appelant** | Le téléchargement de preuve (`DownloadProofAsync`) est rapatrié en WORM par l'appelant via `Archive.Contracts` (jamais `Archive.Domain`, jamais le plug-in — SIG07). |

## Sélecteur au composition root (différence vs l'IdP : OPTIONNEL)

Modèle de l'abstraction IdP (`IIdentityProviderAuthenticator` + sélecteur `AppBootstrap`), **mais la
signature est optionnelle** : `SignatureProviderStartupValidator.Validate(configuredProviders, registry)`
bloque le démarrage **uniquement** pour un fournisseur **configuré** (`Signature:EnabledProviders`) mais
**non câblé** ; **l'absence de tout fournisseur n'est jamais une erreur** (INV-SIGPROV-6 ; un tenant
`Recorded` démarre sans plug-in). Bloquer la plateforme faute de signature serait un durcissement non
justifié (CLAUDE.md n°3).

## Published / Consumed Events

Aucun en SIG03. L'orchestration (demande → suivi → preuve), la règle de gate (SIG06) et la console (SIG10)
arrivent ensuite ; le module générique de validation de document (machine d'états, gate) est porté par
`DocumentApproval` (ADR-0028, SIG04), distinct de ce module.

## Dependencies

- **Aucune** dépendance inter-projet pour `Contracts` (BCL-only).
- `Microsoft.Extensions.DependencyInjection` (framework partagé) — enregistrement du module
  (`Infrastructure`). Aucun package NuGet **nouveau**, aucun ADR (repo-standards §4).

## Layers

- **Contracts** : `ISignatureProvider`, `SignatureProviderCapabilities` (+ enums `SignatureMode` /
  `CompletionTransport` / `SignatureLevel` en `[Flags]`), `SignatureCapability` +
  `SignatureCapabilityNotSupportedResult` (FR), résultats typés (`SignatureRequestResult`,
  `SignatureStatus`, `SignatureProof`, `SignatureWebhookResult`), `SignatureRequest` /
  `SignatureWebhookContext`, `ISignatureProviderFactory` / `ISignatureProviderRegistry`,
  `SignatureProviderAccount` (sans secret en clair).
- **Domain** : marqueur d'assembly (`ISignatureDomainMarker`). Aucune entité en SIG03 (la machine de
  validation vit dans `DocumentApproval` — ADR-0028 ; le hash de binding sur place est une primitive du
  plug-in Wacom — ADR-0030).
- **Application** : marqueur d'assembly (`ISignatureApplicationMarker`). Les handlers (règle de gate,
  ports par purpose) arrivent en SIG06.
- **Infrastructure** : `SignatureProviderRegistry` (résolution par clé), `SignatureModuleRegistration`
  (`AddSignatureModule()`), `SignatureProviderStartupValidator` (optionalité au démarrage).
- **Web** : marqueur d'assembly (`ISignatureWebMarker`). La console signature arrive en SIG10
  (bUnit/Playwright, logique déléguée aux handlers — CLAUDE.md review n°19).
