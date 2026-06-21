# Module FacturX

> Génération du Factur-X (PDF/A-3 + `factur-x.xml` CII EN 16931) côté plateforme — F16 ; ADR-0023 ;
> item FX02 (scaffolding). Liakont produit lui-même le Factur-X comme **format pivot transmis**
> (« toutes les PA comprennent ce format », niveau « Essentiel »), au lieu de déléguer la conversion
> CII au `convert` d'une PA. FX02 livre l'**ossature du module** (4 couches) + le port `IFacturXBuilder`
> + la **frontière de dépendance** (QuestPDF confinée, indépendance PA). Le sérialiseur CII (FX03), le
> scellement PDF/A-3 (FX04), le plug-in PA générique (FXG), la journalisation (FX06) et l'intégration
> pipeline (FX07) arrivent ensuite.

## Purpose

Exposer un port unique `IFacturXBuilder` qui construit, **à partir du pivot EN 16931 seul** (sortie
déterministe du pivot), un `FacturXDocument` (PDF/A-3 scellé + `factur-x.xml` embarqué). Règles
absolues (ADR-0023 ; CLAUDE.md n°1/2/3/14) : la génération **ne dépend d'aucun plug-in PA** et
n'appelle jamais le `convert` d'une PA ; le sérialiseur **n'invente aucune valeur fiscale qualitative**
(catégorie/taux/VATEX recopiés du pivot ; agrégats EN 16931 dérivés par arithmétique normative) ;
montants en `decimal` ; **bloquer plutôt qu'émettre faux**. La décision de générer est prise par le
**pipeline appelant** (FX07), pilotée par la capacité PA `SupportsFacturXTransmission` — jamais par
FacturX, qui ne consulte aucune `PaCapabilities`.

## Boundaries

| Ressource / surface | Accès | Détail |
|---|---|---|
| `IFacturXBuilder` (port) | **abstraction publique** | Couche Application ; entrée = `PivotDocumentDto`, sortie = `FacturXDocument`. |
| `FacturXDocument` (artefact) | **surface publique** | DTO BCL-only (octets PDF/A-3 + `factur-x.xml` + nom de fichier). Consommé par le plug-in générique (FXG) via `IPaClient` étendu (FX07), jamais régénéré. |
| QuestPDF | **interdit hors Infrastructure** | Le scellement PDF/A-3 (FX04) vit dans `FacturX.Infrastructure` (ADR-0023 §1, INV-FX-1) ; aucune fuite vers Contracts/Domain/Application — garde `FacturXBoundaryTests`. |
| Plug-ins PA concrets (`Liakont.PaClients.*`) | **interdit** | La génération est indépendante de toute PA (ADR-0023 INV-FX-4) — garde NetArchTest. |
| `Transmission.Contracts` / `PaCapabilities` | **interdit** | FacturX ne consulte aucune capacité PA ; la décision de générer est prise par le pipeline appelant. |
| Autre module métier | **interdit** | Seule dépendance inter-projet : le contrat agent partagé `Liakont.Agent.Contracts` (le pivot). |

## Published Events

Aucun en FX02 (le port est synchrone, appelé par le pipeline à l'étape Sending — FX07).

## Consumed Events

Aucun en FX02. Le port `IFacturXBuilder` est **appelé à l'étape Sending** (FX07), AVANT la transmission au
plug-in PA, lorsque la PA active déclare `SupportsFacturXTransmission`. Pour respecter la frontière
Contracts-only (le pipeline ne peut référencer la couche `Application` d'un autre module — module-rules §3),
l'appel passe par le **pont `IFacturXArtifactBuilder`** (`Transmission.Contracts`), **implémenté au Host**
en déléguant à `IFacturXBuilder` (cf. F16 §6.1, note d'amendement 2026-06-16). FacturX reste indépendant des
PA (INV-FX-4) : c'est le pipeline appelant, pas FacturX, qui décide de générer.

## Dependencies

- `Liakont.Agent.Contracts` (`PivotDocumentDto` et son graphe pivot) — contrat partagé EN 16931, BCL-only.
- `QuestPDF` (ADR-0023 §1 ; déjà au repo, confiné à `FacturX.Infrastructure`) — scellement PDF/A-3 (FX04).
  Aucun package NuGet **nouveau** : seul un bump `2024.12.3 → 2025.7.4` (correctif compat Mustang).
- `Microsoft.Extensions.DependencyInjection` (framework partagé) — enregistrement du module. Aucun ADR
  (repo-standards §4).

## Layers

- **Contracts** : `FacturXDocument` (artefact : PDF/A-3 + `factur-x.xml` + nom de fichier).
- **Domain** : `FacturXProfile` (constantes de profil NON fiscales — URI XMP, `fx:ConformanceLevel`,
  `/AFRelationship`, nom de pièce jointe — recopiées verbatim d'ADR-0023 §3 / INV-FX-3). Le sérialiseur
  CII maison (pivot → `CrossIndustryInvoice`) arrive en FX03.
- **Application** : `IFacturXBuilder` (port). Les handlers/services de génération arrivent en FX03/FX04.
- **Infrastructure** : `FacturXModuleRegistration` (`AddFacturXModule(IConfiguration)` + déclaration de
  la licence QuestPDF par configuration via `QuestPdf:LicenseType` — contrôle au déploiement, fermé par
  défaut : valeur absente ou non reconnue bloque le démarrage).
  L'implémentation `IFacturXBuilder` (scellement PDF/A-3 QuestPDF) est livrée par FX04.
