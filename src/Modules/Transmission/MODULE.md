# Module Transmission

> Abstraction des Plateformes Agréées (PA) — F05 ; item PAA01. C'EST LE PRODUIT : l'interface qui
> rend Liakont indépendant de toute PA concrète. B2Brouter (PAB), Super PDP (PAS) et tout futur PA
> ne sont que des **plug-ins** de ce framework. PAA01 livre l'ABSTRACTION (Contracts) + le **registre
> de types** (Infrastructure) ; les plug-ins et la suite de contrat arrivent ensuite (PAA02/PAA03).

## Purpose

Définir le contrat unique d'envoi et de suivi vers une PA (`IPaClient`) et le modèle de **capacités
déclarées** (`PaCapabilities`) qui pilote le comportement du produit. Règle absolue (blueprint.md §2 ;
CLAUDE.md n°6/8/16) : **aucune fonctionnalité du produit ne dépend de ce qu'UNE PA sait faire**. Le
produit s'adapte aux capacités déclarées ; il ne désactive jamais une fonctionnalité par un flag
produit ni par un `if (pa is B2Brouter)`. Quand une capacité manque, l'appel retourne un **résultat
typé** (`PaCapabilityNotSupportedResult`), journalisable en français, **jamais une exception** et
**jamais un blocage**.

## Boundaries

| Ressource / surface | Accès | Détail |
|---|---|---|
| `IPaClient` + `PaCapabilities` + DTOs | **abstraction publique** | La seule surface que les plug-ins PA référencent (`Transmission.Contracts`). |
| `IPaClientRegistry` / `IPaClientFactory` | **résolution par type** | Le registre indexe les fabriques par `PaType` (clé), résout le compte PA d'un tenant vers son client. Jamais de `if (type == …)`. |
| Plug-ins PA concrets | **interdit** | Le module ne référence AUCUNE PA concrète (`Liakont.PaClients.*`) — frontière vérifiée par NetArchTest (`TransmissionBoundaryTests`). |
| Types HTTP | **interdit dans l'abstraction** | La construction du payload PA-spécifique vit dans le plug-in (F05 §6) — aucun type HTTP ne traverse `IPaClient`. |
| `PivotDocumentDto` (contrat agent) | **read** | Le document transmis est le pivot ENRICHI (le mapping TVA a déjà écrit `CategoryCode`/`VatexCode` sur `PivotLineTaxDto`). Référence partagée, comme Ingestion/Validation.Contracts. |

Le module **n'accède à aucun autre module métier** (sa seule dépendance inter-projet est le contrat
agent partagé `Liakont.Agent.Contracts`). Les secrets (clé API par tenant, chiffrés) ne transitent
JAMAIS en clair par `PaAccountDescriptor` (CLAUDE.md n°10) : ils sont résolus par le plug-in.

## Published Events

Aucun en PAA01 (l'abstraction est synchrone, appelée par le pipeline).

## Consumed Events

Aucun en PAA01. Le port `IPaClientRegistry` est **appelé directement** par le **pipeline** (PIP,
segment ultérieur) pour résoudre la PA du tenant courant puis envoyer/suivre les documents. Les
résultats `PaCapabilityNotSupportedResult` sont **journalisés** par le module Documents (« en
attente : la plateforme … ne prend pas encore en charge … »).

## Dependencies

- `Liakont.Agent.Contracts` (`PivotDocumentDto` et son graphe pivot) — contrat partagé EN 16931.
- `Microsoft.Extensions.DependencyInjection` (framework partagé) — enregistrement du registre. Aucun
  package NuGet nouveau, aucun ADR (repo-standards §4).

## Layers

- **Contracts** : `IPaClient`, `PaCapabilities`, `PaCapability`, `PaymentReportFlux`,
  `PaymentReportPeriod`, `PaSendState`/`PaSendResult`, `PaDocumentStatus`, `PaTaxReport`/
  `PaTaxReportState`, `PaAccountInfo`, `PaTaxReportSetting`(+`Request`), `PaError`,
  `PaGeneratedDocument`, `PaCapabilityNotSupportedResult`, `IPaClientFactory`, `IPaClientRegistry`,
  `PaAccountDescriptor`.
- **Infrastructure** : `PaClientRegistry` (registre par clé de type) + `AddTransmissionModule()`
  (enregistrement DI, câblé par `Liakont.Host`).
- **Domain / Application** : aucun en PAA01 (pas de logique métier propre au module : la transformation
  vers le format PA vit dans chaque plug-in).
