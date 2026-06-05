# Module Pipeline

> Fondations du pipeline de traitement de la plateforme (PIP01a). Le pipeline orchestre, sur la
> PLATEFORME, le cycle CHECK → SEND → SYNC (déclenché par les événements d'ingestion + des jobs tenant),
> en parlant aux autres modules UNIQUEMENT par leurs `Contracts` et aux Plateformes Agréées via
> `IPaClient` + capacités déclarées (jamais une PA concrète). **PIP01a** a posé les fondations (scaffold,
> lecteur canonique du contenu stagé (PIP00), journal d'exécutions, points d'entrée `Contracts`).
> **PIP01b** livre le premier comportement : le **CHECK** — consommateur durable de `DocumentReceivedV1`
> (mapping TVA → validation → `ReadyToSend`/`Blocked`). SEND (PIP01c) et SYNC (PIP01d) suivent.

## Purpose

Poser les briques manquantes du pipeline sans aucune logique métier :

1. Le **lecteur canonique** `PivotCanonicalJsonReader` qui désérialise le pivot complet relu depuis le
   magasin de staging (PIP00), miroir exact du writer du contrat (ADR-0007) — round-trip sans perte.
2. Le **journal d'exécutions** (`pipeline.run_logs`) : modèle d'écriture `RunLog` (Domain) + table +
   lecture `IPipelineRunQueries`. Écrit par PIP01b+ ; lu par API01 / WEB04.
3. Les **points d'entrée `Contracts`** que CHECK/SEND/SYNC consommeront (exposés chacun par SON module :
   `IValidationService`, `ITvaMappingService`, `IDocumentLifecycle`, `GetCurrentCompanyId`,
   `FindStatusBySourceReferenceAndPayloadHash`).

## Boundaries

| Ressource | Accès | Détail |
|---|---|---|
| Autres modules (Documents, TvaMapping, Validation, TenantSettings, Staging, Archive, Transmission) | **Contracts uniquement** | Le pipeline n'accède à un autre module que par ses `Contracts` (NetArchTest / module-rules §3, CLAUDE.md n°14). Aucune référence `Domain`/`Application`/`Infrastructure` d'un autre module. |
| Plateformes Agréées | **`IPaClient` + capacités** | Le pipeline ne référence JAMAIS un plug-in PA concret (CLAUDE.md n°6/8). Hors périmètre PIP01a. |
| `pipeline.run_logs` | **write (PIP01b+) / read (ici)** | Base DU TENANT (la connexion EST le tenant). Journal d'exécutions, ni table d'audit ni coffre WORM. |

## Published Events

Aucun (PIP01a).

## Consumed Events

- **`DocumentReceivedV1`** (module Ingestion, via l'outbox du socle) — consommé par le **CHECK**
  (`DocumentReceivedConsumer`, PIP01b). Le worker d'outbox dispatche en scope SYSTÈME ; le consommateur
  résout un scope TENANT par slug via `ITenantScopeFactory` (seam du Host, SOL06) et résout depuis ce
  scope les services métier (routés vers la base du tenant). Livraison at-least-once → CHECK idempotent
  (n'agit que sur un document encore `Detected`).

## Dependencies

- `Liakont.Agent.Contracts` : DTO pivot + règles de format canonique (ADR-0007) — le lecteur en est le
  miroir. Utilitaire de sérialisation du contrat, aucune logique métier.
- **Contracts des modules consommés par le CHECK (frontière P1, jamais Domain/Infrastructure)** :
  `Ingestion.Contracts` (`DocumentReceivedV1`), `Staging.Contracts` (`IPayloadStagingStore`),
  `TvaMapping.Contracts` (`ITvaMappingService`), `Validation.Contracts` (`IValidationService`),
  `Documents.Contracts` (`IDocumentLifecycle`, `IDocumentQueries`), `TenantSettings.Contracts`
  (`ITenantSettingsQueries`).
- `Stratum.Common.Infrastructure` / `Stratum.Common.Abstractions` : `IConnectionFactory` (connexion
  tenant), `ITenantScopeFactory` (scope tenant pour le consumer système), `IIntegrationEventConsumer`,
  `MigrationRunner` / `MigrationAssembliesOptions` (DbUp), MediatR (ancre).

## Layers

- **Contracts** : `PipelineRunType`, `PipelineRunTrigger`, `PipelineRunLogDto`, `IPipelineRunQueries`.
- **Domain** : `RunLog` (modèle d'écriture du journal d'exécutions).
- **Application** : `IPipelineApplicationMarker` (ancre MediatR) ; `IPipelineRunLogStore` (écriture du
  journal d'exécutions, PIP01b).
- **Infrastructure** : `PivotCanonicalJsonReader` (lecteur canonique), `PostgresPipelineRunQueries`,
  `PostgresPipelineRunLogStore` (PIP01b), migration `pipeline.run_logs`, `PipelineModuleRegistration` ;
  **`Check/`** (PIP01b) : `DocumentReceivedConsumer` (CHECK), `CheckTvaMapping` (mapping/enrichissement
  pur), `CheckMappingPlan` / `CheckEvaluation`.

## Consumers (segments ultérieurs)

- **PIP01b** (CHECK) : consume `DocumentReceivedV1` → relit le pivot via `PivotCanonicalJsonReader` →
  `ITvaMappingService` → `IValidationService` → `IDocumentLifecycle` ; écrit un `RunLog`.
- **PIP01c** (SEND) / **PIP01d** (SYNC + statut agent + dédoublonnage) : écrivent des `RunLog`.
- **API01** (`GET /runs`) / **WEB04** (page Traitements) : lisent via `IPipelineRunQueries`.

## Cycle de vie & dette connue

`RunLog`, `IPipelineRunQueries` et `pipeline.run_logs` sont des FONDATIONS : le modèle et la table sont
posés ici, mais aucune exécution n'est ÉCRITE en PIP01a (aucun comportement de pipeline). Leurs
producteurs (PIP01b-d) et consommateurs (API01/WEB04) sont nommés ci-dessus.

`ITvaMappingService` (TvaMapping) prend des requêtes de mapping EXPLICITES (code régime + part fournis
par l'appelant) : **la dérivation du `MappingPart` depuis une ligne pivot est une décision fiscale
OUVERTE** (aucune règle sourcée — ADR-0004/F03 §2.3). **Décision PIP01b (CHECK)** : le pipeline générique
fournit `TvaMappingPart.Autre` (« hors du découpage adjudication/frais ») — le choix FIDÈLE à l'absence
de cette distinction dans le pivot générique, JAMAIS deviné (deviner adjudication/frais serait inventer
une règle, CLAUDE.md n°2). Conséquence sûre par défaut : un régime/part absent de la table validée bloque
le document (F03 §4.1). Le découpage adjudication/frais des enchères relève d'une extension future du
contrat pivot (ADR), hors périmètre PIP01b. CHECK n'enrichit que les lignes de forme NON AMBIGUË
(1 code régime ↔ 1 ventilation) ; les autres sont bloquées (aucune association régime→ventilation devinée).
