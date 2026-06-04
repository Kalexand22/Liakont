# Validation Module

> Module métier Liakont (namespace `Liakont.Modules.Validation.*`). Spec : `docs/conception/F04-Controles-Qualite-Validation.md`. Item fondateur : **VAL01** (framework). Règles métier : VAL02-VAL05.

## Purpose

Détecte **avant l'envoi** tout ce qu'une Plateforme Agréée (PA) rejetterait après envoi (F04 §1) :
position `Extract → CHECK → Send`. Un document qui échoue à un contrôle **bloquant** reste `Blocked`
et n'est jamais transmis (« bloquer plutôt qu'envoyer faux », CLAUDE.md n°3).

VAL01 livre le **socle** : le contrat d'une règle (`IDocumentRule`), le résultat agrégé
(`ValidationResult` / `ValidationIssue` / `ValidationSeverity`) et le moteur qui exécute les règles
et agrège leurs anomalies (`ValidationPipeline`). Les règles concrètes arrivent ensuite (identité
VAL02, cohérence VAL03, TVA/avoirs VAL04, garde-fou B2B/B2C VAL05).

Le module **détecte**, il ne **corrige jamais** les données (frontière `Validation` — `module-rules.md` §2).
Toute la logique de validation vit sur la **plateforme**, jamais dans l'agent (CLAUDE.md n°6).

## Boundaries

- **Owns:** le contrat de règle et le moteur d'agrégation (`Contracts` + `Domain`). Aucun schéma de
  base de données : VAL01 est un framework en mémoire, sans persistance propre.
- **Reads:** le document à valider est fourni en entrée (modèle pivot `Liakont.Agent.Contracts.Pivot`,
  lecture seule). Les règles qui ont besoin de paramétrage tenant (profil émetteur, table TVA) le
  liront via les **Contracts** des modules concernés (TenantSettings, TvaMapping), scopé par `CompanyId`.
- **Does NOT:** ne corrige/ne normalise jamais les données (détection seule) ; n'affaiblit jamais une
  anomalie bloquante en alerte (CLAUDE.md n°3) ; n'invente aucune règle fiscale (toute catégorie/seuil
  vient de `docs/conception/F*.md` — CLAUDE.md n°2) ; ne référence aucun module hors de ses `Contracts`.

## Decisions VAL01

- **`IDocumentRule.ValidateAsync` est asynchrone** (la spec F04 §5 illustre une signature synchrone).
  Motif : les règles aval interrogent d'autres modules (unicité du numéro via Documents — VAL03 ;
  couverture du mapping via TvaMapping — VAL04). Un contrat synchrone forcerait une rupture de contrat
  deux items plus loin. La méthode reste pure (sans effet de bord) et retourne la liste vide si conforme.
- **Garantie « jamais de règle silencieuse »** : une règle qui lève une exception est convertie par le
  pipeline en anomalie **bloquante** `RULE_CRASHED` (jamais un passage en succès — CLAUDE.md n°3).
  L'annulation (`OperationCanceledException`) est propagée, jamais convertie en `RULE_CRASHED`.
- **`ValidationResult.IsValid`** (nommage de l'item VAL01) = absence d'anomalie bloquante ; `HasBlockingIssue`
  est l'inverse (le « IsBlocking » de F04 §5). Les alertes (Warning) n'invalident pas le document (F04 §3.3).
- **Persistance différée :** la persistance des anomalies avec le document (F04, console WEB03) relève du
  module **Documents** (lot TRK), absent à ce stade. VAL01 ne porte aucune persistance.

## Decisions VAL02

- **Validateurs élémentaires dans `Domain/Identity`** (`SirenValidator`, `SiretValidator`,
  `FrenchVatNumberValidator`, `CountryCodeValidator`) : fonctions pures, sans dépendance. Algorithmes
  et dérogations (Luhn, exception La Poste 356000000, clé TVA, liste ISO 3166-1 alpha-2) viennent de
  F04 §3.1/§3.2/§4.1/§4.2 — aucune règle inventée (CLAUDE.md n°2). Ils remplaceront à terme la copie
  temporaire `TenantSettings.Domain.Services.SirenValidator` (CFG02) ; consolidation hors périmètre
  VAL02 (frontière inter-modules : TenantSettings ne peut pas référencer `Validation.Domain`).
- **Le SIREN émetteur de référence vient du profil tenant**, lu via `ITenantSettingsQueries`
  (`TenantSettings.Contracts`) scopé par `CompanyId` (note v6, item VAL02) — pas du document. La règle
  vérifie présence + validité + cohérence document↔profil ; le **SIRET émetteur** porté par le document
  est validé s'il est fourni (F04 §3.1). Tout écart est **bloquant** (CLAUDE.md n°3).
- **`FrenchVatNumberValidator` est une brique réutilisable**, pas une règle bloquante VAL02 : F04 §3
  ne liste aucun contrôle de **format** de n° de TVA. Il est livré au titre de l'item VAL02 (point 3)
  et consommé par VAL05 (indice « société » fort) et par un futur contrôle de cohérence TVA. La
  vérification « SIREN intégré au n° TVA = SIREN émetteur » (F04 §4.2) relèvera de cette règle future,
  pas du validateur seul — c'est tracé ici pour éviter toute fausse impression de couverture.
- **`SupplierIdentityRule` / `BuyerIdentityRule`** implémentent `IDocumentRule` (asynchrone côté
  émetteur car il lit le profil tenant ; pur côté acheteur). **Câblage différé** : l'enregistrement DI
  des règles et le branchement du `ValidationPipeline` dans le pipeline d'envoi (lot PIP) ne sont pas
  portés par VAL02 — cohérent avec la posture « persistance/câblage différés » de VAL01. Les règles
  sont consommées par le `ValidationPipeline` existant une fois enregistrées.
- **Code pays = liste ISO 3166-1 alpha-2 officielle (249 codes) embarquée**. `BUYER_COUNTRY_INVALID`
  étant bloquant, une liste incomplète provoquerait un faux blocage : la liste est figée et testée
  (échantillons valides/invalides). Le Kosovo (XK, user-assigned) n'est pas reconnu — évolution par
  mise à jour explicite, jamais devinée. **Compromis V1 assumé** (F04 §6, décision 1/2) : on n'embarque
  pas le moteur genericode/Schematron normatif en V1 ; la dérivation de la codelist genericode versionnée
  (source unique, détection de dérive) est un chantier **phase 2**, au même titre que les artefacts
  FNFE-MPE (F04 §6, décision 2). Le risque de dérive ISO est faible (révisions rares) et borné par les tests.

## Decisions VAL03

- **Règles de cohérence du document (F04 §3.3)** dans `Domain/Rules/` : `LineTotalsRule`
  (Σ lignes HT/TVA = totaux, BLOQUANT), `ArithmeticRule` (BR-CO-15 : TTC = HT + TVA, BLOQUANT, fatale
  EN 16931), `SourceTotalsRule` (total passerelle ≠ total source = ALERTE, décision F04 #3/D5),
  `StructureRule` (≥ 1 ligne, date plausible, devise ISO 4217), `UniquenessRule` (numéro présent et
  non déjà émis pour le tenant).
- **Réconciliation SANS tolérance, en `decimal`, après arrondi half-up 2 décimales** via
  `PivotRounding.RoundAmount` (CLAUDE.md n°1 ; EN 16931 : aucune tolérance sur l'arithmétique). Un
  écart d'un centime BLOQUE — jamais de rattrapage silencieux.
- **Niveaux de sévérité repris de la spec, jamais durcis ni affaiblis** (CLAUDE.md n°2, n°3) :
  l'écart passerelle/source est un Warning PARCE QUE la spec le dit (F04 §3.3, décision #3), pas par
  affaiblissement ; date future = BLOQUANT, date < 2000 = ALERTE (décision F04 #4).
- **Devise validée contre un référentiel ISO 4217** (`Iso4217Currencies`) — donnée de référence
  (genericode v17.0, F04 §2.4), pas une règle fiscale inventée. Comparaison insensible à la casse.
- **Horloge injectée (`TimeProvider`)** dans `StructureRule` pour une détection « date dans le
  futur » déterministe et testable.
- **Anti-doublon via le port `IIssuedDocumentLookup`** (Contracts) : Validation DÉCLARE le besoin,
  l'implémentation réelle est livrée par le module Documents/Tracking (TRK03). Tant qu'il n'existe
  pas, un faux d'essai suffit (acceptance VAL03). La frontière Contracts-only est préservée.
- **Pas encore de câblage DI** : comme VAL01, les règles sont des classes `Domain` testées en
  instanciation directe. Leur enregistrement dans le conteneur (pour alimenter `ValidationPipeline`)
  se fera au moment où le pipeline d'envoi est composé (Host / lot PIP) — prématuré ici.

## Published Events

Aucun. (Le résultat de validation est consommé par le pipeline d'envoi — lot PIP — via les `Contracts`.)

## Consumed Events

Aucun.

## Dependencies

- `Liakont.Agent.Contracts` (modèle pivot du document en entrée — `PivotDocumentDto`, `PivotRounding`).
- `Liakont.Modules.TenantSettings.Contracts` (VAL02) : lecture du profil émetteur du tenant
  (`ITenantSettingsQueries.GetTenantProfile`), scopée par `CompanyId`. Frontière inter-modules
  **Contracts-only** respectée (`module-rules.md` §3).
- Le besoin d'unicité de numéro (VAL03) est exprimé par le port `IIssuedDocumentLookup` (dans
  **Validation.Contracts**), implémenté à l'exécution par le module Documents/Tracking (TRK03) —
  inversion de dépendance (aucune référence sortante de Validation pour ce besoin).
