# Module Pipeline

> Fondations du pipeline de traitement de la plateforme (PIP01a). Le pipeline orchestre, sur la
> PLATEFORME, le cycle CHECK → SEND → SYNC (déclenché par les événements d'ingestion + des jobs tenant),
> en parlant aux autres modules UNIQUEMENT par leurs `Contracts` et aux Plateformes Agréées via
> `IPaClient` + capacités déclarées (jamais une PA concrète). **PIP01a ne livre AUCUN comportement de
> pipeline** : seulement le scaffold du module, le lecteur canonique du contenu stagé (PIP00), et les
> points d'entrée `Contracts` que CHECK/SEND/SYNC (PIP01b-d) consommeront.

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

Aucun en PIP01a. Le consumer durable de `DocumentReceivedV1` (CHECK) arrive en PIP01b.

## Dependencies

- `Liakont.Agent.Contracts` : DTO pivot + règles de format canonique (ADR-0007) — le lecteur en est le
  miroir. Utilitaire de sérialisation du contrat, aucune logique métier.
- `Stratum.Common.Infrastructure` : `IConnectionFactory` (connexion tenant), `MigrationRunner` /
  `MigrationAssembliesOptions` (DbUp), MediatR (ancre).

## Layers

- **Contracts** : `PipelineRunType`, `PipelineRunTrigger`, `PipelineRunLogDto`, `IPipelineRunQueries`.
- **Domain** : `RunLog` (modèle d'écriture du journal d'exécutions).
- **Application** : `IPipelineApplicationMarker` (ancre MediatR ; handlers CHECK/SEND/SYNC à venir).
- **Infrastructure** : `PivotCanonicalJsonReader` (lecteur canonique), `PostgresPipelineRunQueries`,
  migration `pipeline.run_logs`, `PipelineModuleRegistration`.

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
OUVERTE** (aucune règle sourcée — ADR-0004/F03 §2.3), déférée à PIP01b (CHECK). PIP01a n'invente aucune
règle fiscale (CLAUDE.md n°2) : il expose le moteur `TvaMapper` existant à la frontière, rien de plus.
